using System.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;
using SpeedSaga.API.Hubs;
using SpeedSaga.API.Infrastructure;
using SpeedSaga.API.Models;

namespace SpeedSaga.API.Services;

public interface IGameService
{
    Task<ApiResponse<object>> SubmitResultAsync(Guid playerId, SubmitResultRequest req);
    Task<ApiResponse<object>> JoinMatchAsync(Guid playerId, JoinMatchRequest req);
    Task<object?> GetMatchStatusAsync(Guid playerId, string connId);
    Task SyncMoveAsync(Guid playerId, string sessionId, string direction, int col, int row, float timestamp);
    Task<object?> GetSessionMovesAsync(Guid playerId, string sessionId, int afterIndex);
    Task<object?> GetSessionStatusAsync(Guid playerId, string sessionId);
    Task<ApiResponse<object>> StartSinglePlayerAsync(Guid playerId, StartSinglePlayerRequest req);
    Task<ApiResponse<object>> StartFreePlayAsync(Guid playerId, StartFreePlayRequest req);
    Task<object?> GetReplayAsync(Guid sessionId, Guid? playerId);
    Task<object?> GetGameHistoryAsync(Guid playerId, int page = 1, int pageSize = 50);
    Task RecalculateWinRates();
    Task CleanupExpiredQueue();
    Task CleanupStaleTwoPlayerSessionsAsync();
    Task ForfeitPlayerAsync(Guid sessionId, Guid playerId);
}

public class GameService : IGameService
{
    private readonly ISqlConnectionFactory _db;
    private readonly IWalletService _wallet;
    private readonly ILevelService _level;
    private readonly IHubContext<GameHub> _hub;
    private readonly SessionMoveStore _moves;
    private readonly INotificationService _notifications;
    private readonly IMovePersistenceQueue _moveQueue;

    public GameService(ISqlConnectionFactory db, IWalletService wallet, ILevelService level, IHubContext<GameHub> hub, SessionMoveStore moves, INotificationService notifications, IMovePersistenceQueue moveQueue)
    {
        _db = db;
        _wallet = wallet;
        _level = level;
        _hub = hub;
        _moves = moves;
        _notifications = notifications;
        _moveQueue = moveQueue;
    }

    public async Task<ApiResponse<object>> StartSinglePlayerAsync(Guid playerId, StartSinglePlayerRequest req)
    {
        if (!await _wallet.HasSufficientBalanceAsync(playerId, req.EntryFeePaise))
            return new ApiResponse<object>(false, "Insufficient balance.");

        var level = await _level.AllocateLevelAsync(playerId, new AllocateLevelRequest(req.TimeMode, req.RewardMode, req.EntryFeePaise));
        if (level == null || !PuzzleTemplateProvider.IsValidGridJson(level.GridJson))
            return new ApiResponse<object>(false, "No level available for the selected mode.");

        var rewardPaise = req.RewardMode == "5x" ? req.EntryFeePaise * 5 : req.EntryFeePaise * 3;
        var timeLimitSecs = ResolveTimeLimit(req.TimeMode, req.RewardMode);
        var sessionId = Guid.NewGuid();
        var gameMode = req.RewardMode == "5x" ? "SinglePlayer5x" : "SinglePlayer3x";

        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();

        await using (var insert = new SqlCommand(@"
            INSERT INTO GameSessions (SessionId, Player1Id, GameMode, RewardMode, EntryFeePaise, RewardPaise, LevelId, TimeLimitSecs, Status, StartedAt)
            VALUES (@SessionId, @PlayerId, @GameMode, @RewardMode, @EntryFee, @Reward, @LevelId, @TimeLimit, 'Active', GETDATE())", cn))
        {
            insert.Parameters.AddWithValue("@SessionId", sessionId);
            insert.Parameters.AddWithValue("@PlayerId", playerId);
            insert.Parameters.AddWithValue("@GameMode", gameMode);
            insert.Parameters.AddWithValue("@RewardMode", req.RewardMode);
            insert.Parameters.AddWithValue("@EntryFee", req.EntryFeePaise);
            insert.Parameters.AddWithValue("@Reward", rewardPaise);
            insert.Parameters.AddWithValue("@LevelId", level.LevelId);
            insert.Parameters.AddWithValue("@TimeLimit", timeLimitSecs);
            await insert.ExecuteNonQueryAsync();
        }

        var deduct = await _wallet.DeductEntryFeeAsync(playerId, sessionId, req.EntryFeePaise);
        if (!deduct.Success)
        {
            await using var cancel = new SqlCommand("DELETE FROM GameSessions WHERE SessionId=@SessionId", cn);
            cancel.Parameters.AddWithValue("@SessionId", sessionId);
            await cancel.ExecuteNonQueryAsync();
            return deduct;
        }

        _ = RecordLevelPlayedAsync(playerId, level.LevelId, sessionId, req.EntryFeePaise, req.RewardMode);

        return new ApiResponse<object>(true, "Single player session started", new
        {
            SessionId = sessionId,
            level.LevelId,
            level.GridJson,
            PuzzleTier = level.PuzzleTier,
            TargetArrows = level.TargetArrows,
            RewardPaise = rewardPaise,
            TimeLimitSecs = timeLimitSecs
        });
    }

    public async Task<ApiResponse<object>> StartFreePlayAsync(Guid playerId, StartFreePlayRequest req)
    {
        var level = await _level.AllocateLevelAsync(playerId, new AllocateLevelRequest(req.TimeMode, "3x"));
        if (level == null)
            return new ApiResponse<object>(false, "No level available for the selected mode.");

        var timeLimitSecs = ParseTimeMode(req.TimeMode);
        var sessionId = Guid.NewGuid();

        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var insert = new SqlCommand(@"
            INSERT INTO GameSessions (SessionId, Player1Id, GameMode, EntryFeePaise, RewardPaise, LevelId, TimeLimitSecs, Status, StartedAt)
            VALUES (@SessionId, @PlayerId, 'FreePlay', 0, 0, @LevelId, @TimeLimit, 'Active', GETDATE())", cn);
        insert.Parameters.AddWithValue("@SessionId", sessionId);
        insert.Parameters.AddWithValue("@PlayerId", playerId);
        insert.Parameters.AddWithValue("@LevelId", level.LevelId);
        insert.Parameters.AddWithValue("@TimeLimit", timeLimitSecs);
        await insert.ExecuteNonQueryAsync();

        _ = RecordLevelPlayedAsync(playerId, level.LevelId, sessionId, 0, "3x");

        return new ApiResponse<object>(true, "Free play session started", new
        {
            SessionId = sessionId,
            level.LevelId,
            level.GridJson,
            PuzzleTier = level.PuzzleTier,
            TargetArrows = level.TargetArrows,
            TimeLimitSecs = timeLimitSecs
        });
    }

    public async Task<ApiResponse<object>> SubmitResultAsync(Guid playerId, SubmitResultRequest req)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_SubmitGameResult", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@SessionId", req.SessionId);
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        cmd.Parameters.AddWithValue("@IsWon", req.IsWon);
        cmd.Parameters.AddWithValue("@SolveSecs", req.SolveSecs);
        cmd.Parameters.AddWithValue("@MovesJson", (object?)req.MovesJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@TotalMoves", req.TotalMoves);

        var pRes = cmd.Parameters.Add("@Result", SqlDbType.Int);
        pRes.Direction = ParameterDirection.Output;
        var pMsg = cmd.Parameters.Add("@Message", SqlDbType.NVarChar, 200);
        pMsg.Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync();

        var r = (int)pRes.Value!;
        if (r == 1)
        {
            _moves.Clear(req.SessionId.ToString());
            await UpdateTournamentScoreIfNeededAsync(playerId, req.SessionId, req.IsWon, req.SolveSecs, req.TotalMoves);
            await SendGameResultNotificationsAsync(playerId, req.SessionId, req.IsWon);
            return new ApiResponse<object>(true, (string)pMsg.Value!);
        }
        return new ApiResponse<object>(false, (string)pMsg.Value!);
    }

    async Task UpdateTournamentScoreIfNeededAsync(Guid playerId, Guid sessionId, bool isWon, int solveSecs, int totalMoves)
    {
        if (!isWon) return;
        try
        {
            await using var cn = _db.CreateConnection();
            await cn.OpenAsync();
            await using var cmd = new SqlCommand(
                "SELECT TournamentId FROM GameSessions WHERE SessionId = @Sid AND GameMode = 'Tournament' AND TournamentId IS NOT NULL", cn);
            cmd.Parameters.AddWithValue("@Sid", sessionId);
            var tid = await cmd.ExecuteScalarAsync();
            if (tid == null || tid == DBNull.Value) return;
            await using var upd = new SqlCommand("USP_UpdateTournamentScore", cn) { CommandType = CommandType.StoredProcedure };
            upd.Parameters.AddWithValue("@TournamentId", (Guid)tid);
            upd.Parameters.AddWithValue("@PlayerId", playerId);
            upd.Parameters.AddWithValue("@SessionId", sessionId);
            upd.Parameters.AddWithValue("@SolveSecs", solveSecs);
            upd.Parameters.AddWithValue("@TotalMoves", totalMoves);
            await upd.ExecuteNonQueryAsync();
        }
        catch { /* best effort */ }
    }

    async Task SendGameResultNotificationsAsync(Guid playerId, Guid sessionId, bool isWon)
    {
        try
        {
            await using var cn = _db.CreateConnection();
            await cn.OpenAsync();
            await using var cmd = new SqlCommand(@"
                SELECT Player1Id, Player2Id, RewardPaise, GameMode FROM GameSessions WHERE SessionId = @SessionId", cn);
            cmd.Parameters.AddWithValue("@SessionId", sessionId);
            await using var rdr = await cmd.ExecuteReaderAsync();
            if (!await rdr.ReadAsync()) return;

            var p1 = (Guid)rdr["Player1Id"];
            var p2 = rdr["Player2Id"] == DBNull.Value ? (Guid?)null : (Guid)rdr["Player2Id"];
            var reward = rdr["RewardPaise"] == DBNull.Value ? 0L : Convert.ToInt64(rdr["RewardPaise"]);
            var mode = rdr["GameMode"]?.ToString() ?? "";

            if (mode == "FreePlay") return;

            if (isWon)
            {
                var rewardRs = reward / 100.0;
                await _notifications.SendAsync(playerId, "You won!",
                    reward > 0 ? $"Rs {rewardRs:0} credited to your wallet." : "Great solve!", "game_win");
                var opponent = p1 == playerId ? p2 : p1;
                if (opponent.HasValue)
                    await _notifications.SendAsync(opponent.Value, "Match lost",
                        "Your opponent solved the puzzle first.", "game_loss");
            }
            else
            {
                await _notifications.SendAsync(playerId, "Match lost",
                    "Better luck next time!", "game_loss");
            }
        }
        catch { /* notifications are best-effort */ }
    }

    public async Task<ApiResponse<object>> JoinMatchAsync(Guid playerId, JoinMatchRequest req)
    {
        if (!await _wallet.HasSufficientBalanceAsync(playerId, req.EntryFeePaise))
            return new ApiResponse<object>(false, "Insufficient balance.");

        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_MatchmakingJoin", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        cmd.Parameters.AddWithValue("@FeePaise", req.EntryFeePaise);
        cmd.Parameters.AddWithValue("@TimeSecs", req.TimeSecs);
        cmd.Parameters.AddWithValue("@ConnId", req.SignalRConnId);

        var pSess = cmd.Parameters.Add("@SessionId", SqlDbType.UniqueIdentifier);
        pSess.Direction = ParameterDirection.Output;
        var pNew = cmd.Parameters.Add("@IsNewSession", SqlDbType.Bit);
        pNew.Direction = ParameterDirection.Output;
        var pOpp = cmd.Parameters.Add("@OpponentId", SqlDbType.UniqueIdentifier);
        pOpp.Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync();

        var isNew = (bool)pNew.Value!;
        Guid? sessionId = pSess.Value == DBNull.Value ? null : (Guid)pSess.Value;
        Guid? opponentId = pOpp.Value == DBNull.Value ? null : (Guid)pOpp.Value;

        if (!isNew && sessionId.HasValue)
        {
            var deduct = await _wallet.DeductEntryFeeAsync(playerId, sessionId.Value, req.EntryFeePaise);
            if (!deduct.Success)
                return deduct;

            if (opponentId.HasValue)
            {
                var oppDeduct = await _wallet.DeductEntryFeeAsync(opponentId.Value, sessionId.Value, req.EntryFeePaise);
                if (!oppDeduct.Success)
                    return oppDeduct;
            }

            var timeMode = SecsToTimeMode(req.TimeSecs);
            var level = await _level.AllocateLevelAsync(playerId, new AllocateLevelRequest(timeMode, "3x", req.EntryFeePaise));
            if (level != null)
            {
                await SetSessionLevelAsync(sessionId.Value, level.LevelId);
                await RecordLevelPlayedAsync(playerId, level.LevelId, sessionId.Value, req.EntryFeePaise, "3x");
                if (opponentId.HasValue)
                    await RecordLevelPlayedAsync(opponentId.Value, level.LevelId, sessionId.Value, req.EntryFeePaise, "3x");
            }

            var waitConn = opponentId.HasValue
                ? await GetPlayerConnIdAsync(opponentId.Value, sessionId.Value)
                : null;
            var rewardPaise = req.EntryFeePaise * 2 * 85 / 100;
            var matchPayload = new
            {
                SessionId = sessionId,
                IsWaiting = false,
                OpponentId = opponentId,
                LevelId = level?.LevelId,
                GridJson = level?.GridJson,
                TimeLimitSecs = req.TimeSecs,
                RewardPaise = rewardPaise
            };

            if (!string.IsNullOrEmpty(waitConn))
                await _hub.Clients.Client(waitConn).SendAsync("MatchFound", matchPayload);
            if (!string.IsNullOrEmpty(req.SignalRConnId))
                await _hub.Clients.Client(req.SignalRConnId).SendAsync("MatchFound", matchPayload);

            return new ApiResponse<object>(true, "Match found", matchPayload);
        }

        return new ApiResponse<object>(true, "Waiting for opponent", new
        {
            SessionId = sessionId,
            IsWaiting = true,
            OpponentId = opponentId
        });
    }

    public async Task<object?> GetMatchStatusAsync(Guid playerId, string connId)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_GetMatchStatus", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        cmd.Parameters.AddWithValue("@ConnId", connId);
        var pSess = cmd.Parameters.Add("@SessionId", SqlDbType.UniqueIdentifier);
        pSess.Direction = ParameterDirection.Output;
        var pMatch = cmd.Parameters.Add("@IsMatched", SqlDbType.Bit);
        pMatch.Direction = ParameterDirection.Output;
        var pLevel = cmd.Parameters.Add("@LevelId", SqlDbType.Int);
        pLevel.Direction = ParameterDirection.Output;
        var pGrid = cmd.Parameters.Add("@GridJson", SqlDbType.NVarChar, -1);
        pGrid.Direction = ParameterDirection.Output;
        var pTime = cmd.Parameters.Add("@TimeLimit", SqlDbType.Int);
        pTime.Direction = ParameterDirection.Output;
        var pReward = cmd.Parameters.Add("@RewardPaise", SqlDbType.BigInt);
        pReward.Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync();

        var isMatched = pMatch.Value != DBNull.Value && (bool)pMatch.Value;
        if (!isMatched) return new { IsWaiting = true };

        return new
        {
            IsWaiting = false,
            SessionId = pSess.Value == DBNull.Value ? null : pSess.Value.ToString(),
            LevelId = pLevel.Value == DBNull.Value ? (int?)null : (int)pLevel.Value,
            GridJson = pGrid.Value?.ToString(),
            TimeLimitSecs = pTime.Value == DBNull.Value ? 60 : (int)pTime.Value,
            RewardPaise = pReward.Value == DBNull.Value ? 0L : (long)pReward.Value
        };
    }

    async Task SetSessionLevelAsync(Guid sessionId, int levelId)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("UPDATE GameSessions SET LevelId = @LevelId WHERE SessionId = @SessionId", cn);
        cmd.Parameters.AddWithValue("@LevelId", levelId);
        cmd.Parameters.AddWithValue("@SessionId", sessionId);
        await cmd.ExecuteNonQueryAsync();
    }

    async Task<string?> GetPlayerConnIdAsync(Guid playerId, Guid sessionId)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(@"
            SELECT TOP 1 SignalRConnId FROM MatchmakingQueue
            WHERE PlayerId = @PlayerId AND MatchedSessionId = @SessionId", cn);
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        cmd.Parameters.AddWithValue("@SessionId", sessionId);
        var result = await cmd.ExecuteScalarAsync();
        return result?.ToString();
    }

    static string SecsToTimeMode(int secs) => secs switch
    {
        120 => "2min",
        180 => "3min",
        240 => "4min",
        300 => "5min",
        _ => "1min"
    };

    public Task SyncMoveAsync(Guid playerId, string sessionId, string direction, int col, int row, float timestamp)
    {
        var moveIndex = _moves.AddMove(sessionId, playerId, direction, col, row, timestamp);

        if (Guid.TryParse(sessionId, out var sid))
            _moveQueue.Enqueue(new PendingMove(sid, playerId, moveIndex, direction, col, row, timestamp));

        _ = _hub.Clients.Group(sessionId).SendAsync("OpponentMoved", direction, col, row, timestamp);
        return Task.CompletedTask;
    }

    async Task RecordLevelPlayedAsync(Guid playerId, int levelId, Guid sessionId, long entryFeePaise, string rewardMode)
    {
        try
        {
            await using var cn = _db.CreateConnection();
            await cn.OpenAsync();
            await using var cmd = new SqlCommand("USP_RecordLevelPlayed", cn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@PlayerId", playerId);
            cmd.Parameters.AddWithValue("@LevelId", levelId);
            cmd.Parameters.AddWithValue("@SessionId", sessionId);
            cmd.Parameters.AddWithValue("@EntryFeePaise", entryFeePaise);
            cmd.Parameters.AddWithValue("@RewardMode", rewardMode);
            await cmd.ExecuteNonQueryAsync();
        }
        catch { /* best-effort */ }
    }

    public async Task<object?> GetSessionMovesAsync(Guid playerId, string sessionId, int afterIndex)
    {
        var cached = _moves.GetMoves(sessionId, playerId, afterIndex);
        if (cached.Count > 0)
            return cached.Select(m => new { m.Index, m.Direction, m.Col, m.Row, m.Timestamp }).ToList();

        if (!Guid.TryParse(sessionId, out var sid)) return Array.Empty<object>();
        return await GetSessionMovesFromDbAsync(sid, playerId, afterIndex);
    }

    async Task<List<object>> GetSessionMovesFromDbAsync(Guid sessionId, Guid viewerId, int afterIndex)
    {
        var items = new List<object>();
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(@"
            SELECT MoveIndex, Direction, Col, Row, Timestamp
            FROM SessionMoves WITH (NOLOCK)
            WHERE SessionId = @SessionId AND PlayerId <> @ViewerId AND MoveIndex > @AfterIndex
            ORDER BY MoveIndex", cn);
        cmd.Parameters.AddWithValue("@SessionId", sessionId);
        cmd.Parameters.AddWithValue("@ViewerId", viewerId);
        cmd.Parameters.AddWithValue("@AfterIndex", afterIndex);
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            items.Add(new
            {
                Index = (int)rdr["MoveIndex"],
                Direction = rdr["Direction"].ToString(),
                Col = (int)rdr["Col"],
                Row = (int)rdr["Row"],
                Timestamp = Convert.ToSingle(rdr["Timestamp"])
            });
        }
        return items;
    }

    public async Task<object?> GetSessionStatusAsync(Guid playerId, string sessionId)
    {
        if (!Guid.TryParse(sessionId, out var sid)) return null;
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(@"
            SELECT Status, WinnerId, Player1Id, Player2Id
            FROM GameSessions WHERE SessionId = @SessionId", cn);
        cmd.Parameters.AddWithValue("@SessionId", sid);
        await using var rdr = await cmd.ExecuteReaderAsync();
        if (!await rdr.ReadAsync()) return null;

        var status = rdr["Status"]?.ToString() ?? "Active";
        var winnerId = rdr["WinnerId"] == DBNull.Value ? (Guid?)null : (Guid)rdr["WinnerId"];
        var p1 = (Guid)rdr["Player1Id"];
        var p2 = rdr["Player2Id"] == DBNull.Value ? (Guid?)null : (Guid)rdr["Player2Id"];

        return new
        {
            Status = status,
            IsComplete = status == "Complete",
            WinnerId = winnerId?.ToString(),
            YouWon = winnerId.HasValue && winnerId.Value == playerId,
            OpponentWon = winnerId.HasValue && winnerId.Value != playerId
        };
    }

    public async Task<object?> GetReplayAsync(Guid sessionId, Guid? playerId)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_GetReplay", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@SessionId", sessionId);
        cmd.Parameters.AddWithValue("@PlayerId", (object?)playerId ?? DBNull.Value);
        await using var rdr = await cmd.ExecuteReaderAsync();
        if (!await rdr.ReadAsync()) return null;

        var movesJson = rdr["MovesJson"]?.ToString();
        var totalMoves = rdr["TotalMoves"] == DBNull.Value ? 0 : Convert.ToInt32(rdr["TotalMoves"]);
        var solvedInSecs = rdr["SolvedInSecs"] == DBNull.Value ? (int?)null : Convert.ToInt32(rdr["SolvedInSecs"]);
        var levelId = rdr["LevelId"] == DBNull.Value ? (int?)null : (int)rdr["LevelId"];
        var replayPlayer = rdr["PlayerId"] == DBNull.Value ? playerId : (Guid)rdr["PlayerId"];
        var timeLimitSecs = rdr["TimeLimitSecs"] == DBNull.Value ? (int?)null : Convert.ToInt32(rdr["TimeLimitSecs"]);
        var gameMode = rdr["GameMode"]?.ToString();
        var entryFeePaise = rdr["EntryFeePaise"] == DBNull.Value ? 0L : Convert.ToInt64(rdr["EntryFeePaise"]);
        var rewardPaise = rdr["RewardPaise"] == DBNull.Value ? 0L : Convert.ToInt64(rdr["RewardPaise"]);
        var startedAt = rdr["StartedAt"] == DBNull.Value ? (DateTime?)null : (DateTime)rdr["StartedAt"];
        var completedAt = rdr["CompletedAt"] == DBNull.Value ? (DateTime?)null : (DateTime)rdr["CompletedAt"];

        string? gridJson = null;
        string? puzzleTier = null;
        if (levelId.HasValue)
        {
            await rdr.CloseAsync();
            await using var levelCmd = new SqlCommand(@"
                SELECT GridJson, DifficultyScore FROM Levels WHERE LevelId = @LevelId", cn);
            levelCmd.Parameters.AddWithValue("@LevelId", levelId.Value);
            await using var levelRdr = await levelCmd.ExecuteReaderAsync();
            if (await levelRdr.ReadAsync())
            {
                gridJson = levelRdr["GridJson"]?.ToString();
                var score = levelRdr["DifficultyScore"] == DBNull.Value ? 30 : (int)levelRdr["DifficultyScore"];
                puzzleTier = score switch
                {
                    >= 80 => "SuperHard",
                    >= 60 => "Hard",
                    >= 40 => "Medium",
                    _ => "Easy"
                };
            }
        }

        if (string.IsNullOrWhiteSpace(gridJson) && levelId.HasValue && replayPlayer.HasValue)
        {
            var generated = PuzzleTemplateProvider.Generate("Medium", levelId.Value, replayPlayer.Value);
            gridJson = generated.GridJson;
            puzzleTier = generated.PuzzleTier;
        }

        return new
        {
            MovesJson = movesJson,
            TotalMoves = totalMoves,
            SolvedInSecs = solvedInSecs,
            LevelId = levelId,
            GridJson = gridJson,
            PuzzleTier = puzzleTier,
            TimeLimitSecs = timeLimitSecs,
            GameMode = gameMode,
            EntryFeePaise = entryFeePaise,
            RewardPaise = rewardPaise,
            StartedAt = startedAt,
            CompletedAt = completedAt
        };
    }

    public async Task ForfeitPlayerAsync(Guid sessionId, Guid playerId)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_ForfeitTwoPlayerSession", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@SessionId", sessionId);
        cmd.Parameters.AddWithValue("@ForfeitPlayerId", playerId);
        var pRes = cmd.Parameters.Add("@Result", SqlDbType.Int);
        pRes.Direction = ParameterDirection.Output;
        var pMsg = cmd.Parameters.Add("@Message", SqlDbType.NVarChar, 200);
        pMsg.Direction = ParameterDirection.Output;
        var pWin = cmd.Parameters.Add("@WinnerId", SqlDbType.UniqueIdentifier);
        pWin.Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task CleanupStaleTwoPlayerSessionsAsync()
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_CleanupStaleTwoPlayerSessions", cn) { CommandType = CommandType.StoredProcedure };
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<object?> GetGameHistoryAsync(Guid playerId, int page = 1, int pageSize = 50)
    {
        var items = new List<object>();
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_GetPlayerGameHistory", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        cmd.Parameters.AddWithValue("@Page", page);
        cmd.Parameters.AddWithValue("@PageSize", Math.Clamp(pageSize, 1, 100));
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            items.Add(new
            {
                SessionId = rdr["SessionId"].ToString(),
                GameMode = rdr["GameMode"]?.ToString(),
                RewardMode = rdr["RewardMode"]?.ToString(),
                EntryFeePaise = Convert.ToInt64(rdr["EntryFeePaise"]),
                RewardPaise = Convert.ToInt64(rdr["RewardPaise"]),
                LevelId = rdr["LevelId"] == DBNull.Value ? (int?)null : (int)rdr["LevelId"],
                TimeLimitSecs = rdr["TimeLimitSecs"] == DBNull.Value ? 0 : Convert.ToInt32(rdr["TimeLimitSecs"]),
                Status = rdr["Status"]?.ToString(),
                StartedAt = rdr["StartedAt"] == DBNull.Value ? (DateTime?)null : (DateTime)rdr["StartedAt"],
                CompletedAt = rdr["CompletedAt"] == DBNull.Value ? (DateTime?)null : (DateTime)rdr["CompletedAt"],
                IsWon = Convert.ToInt32(rdr["IsWon"]) != 0,
                TotalMoves = rdr["TotalMoves"] == DBNull.Value ? 0 : Convert.ToInt32(rdr["TotalMoves"]),
                SolvedInSecs = rdr["SolvedInSecs"] == DBNull.Value ? (int?)null : Convert.ToInt32(rdr["SolvedInSecs"]),
                IsReplayAvailable = rdr["IsReplayAvailable"] != DBNull.Value && Convert.ToBoolean(rdr["IsReplayAvailable"]),
                RecordedMoves = rdr["RecordedMoves"] == DBNull.Value ? 0 : Convert.ToInt32(rdr["RecordedMoves"])
            });
        }
        return items;
    }

    public async Task RecalculateWinRates()
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_RecalculateWinRates", cn) { CommandType = CommandType.StoredProcedure };
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task CleanupExpiredQueue()
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_CleanupExpiredQueue", cn) { CommandType = CommandType.StoredProcedure };
        await cmd.ExecuteNonQueryAsync();
    }

    private static int ResolveTimeLimit(string timeMode, string rewardMode)
    {
        int baseSecs = ParseTimeMode(timeMode);
        if (rewardMode == "5x") return Math.Max(1, baseSecs / 2);
        return Math.Max(1, (int)(baseSecs * 0.75)); // 3x and default
    }

    private static int ParseTimeMode(string timeMode) => timeMode switch
    {
        "1min" => 60,
        "2min" => 120,
        "3min" => 180,
        "4min" => 240,
        "5min" => 300,
        _ => 60
    };
}
