using System.Data;
using Microsoft.Data.SqlClient;
using SpeedSaga.API.Infrastructure;
using SpeedSaga.API.Models;

namespace SpeedSaga.API.Services;

public interface IGameService
{
    Task<ApiResponse<object>> SubmitResultAsync(Guid playerId, SubmitResultRequest req);
    Task<ApiResponse<object>> JoinMatchAsync(Guid playerId, JoinMatchRequest req);
    Task<ApiResponse<object>> StartSinglePlayerAsync(Guid playerId, StartSinglePlayerRequest req);
    Task<ApiResponse<object>> StartFreePlayAsync(Guid playerId, StartFreePlayRequest req);
    Task<object?> GetReplayAsync(Guid sessionId, Guid? playerId);
    Task RecalculateWinRates();
    Task CleanupExpiredQueue();
}

public class GameService : IGameService
{
    private readonly ISqlConnectionFactory _db;
    private readonly IWalletService _wallet;
    private readonly ILevelService _level;

    public GameService(ISqlConnectionFactory db, IWalletService wallet, ILevelService level)
    {
        _db = db;
        _wallet = wallet;
        _level = level;
    }

    public async Task<ApiResponse<object>> StartSinglePlayerAsync(Guid playerId, StartSinglePlayerRequest req)
    {
        if (!await _wallet.HasSufficientBalanceAsync(playerId, req.EntryFeePaise))
            return new ApiResponse<object>(false, "Insufficient balance.");

        var level = await _level.AllocateLevelAsync(playerId, new AllocateLevelRequest(req.TimeMode, req.RewardMode));
        if (level == null)
            return new ApiResponse<object>(false, "No level available for the selected mode.");

        var rewardPaise = req.RewardMode == "5x" ? req.EntryFeePaise * 5 : req.EntryFeePaise * 3;
        var timeLimitSecs = ParseTimeMode(req.TimeMode);
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
            return deduct;

        return new ApiResponse<object>(true, "Single player session started", new
        {
            SessionId = sessionId,
            level.LevelId,
            level.GridJson,
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

        return new ApiResponse<object>(true, "Free play session started", new
        {
            SessionId = sessionId,
            level.LevelId,
            level.GridJson,
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
        return r == 1
            ? new ApiResponse<object>(true, (string)pMsg.Value!)
            : new ApiResponse<object>(false, (string)pMsg.Value!);
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
        }

        return new ApiResponse<object>(true, isNew ? "Waiting for opponent" : "Match found", new
        {
            SessionId = sessionId,
            IsWaiting = isNew,
            OpponentId = opponentId
        });
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

        return new
        {
            MovesJson = rdr["MovesJson"].ToString(),
            TotalMoves = (int)rdr["TotalMoves"],
            SolvedInSecs = rdr["SolvedInSecs"] == DBNull.Value ? (int?)null : (int)rdr["SolvedInSecs"],
            LevelId = rdr["LevelId"] == DBNull.Value ? (int?)null : (int)rdr["LevelId"],
            TimeLimitSecs = rdr["TimeLimitSecs"] == DBNull.Value ? (int?)null : (int)rdr["TimeLimitSecs"],
            GameMode = rdr["GameMode"]?.ToString()
        };
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
