using System.Data;
using Microsoft.Data.SqlClient;
using SpeedSaga.API.Infrastructure;
using SpeedSaga.API.Models;

namespace SpeedSaga.API.Services;

public interface ITournamentService
{
    Task<object?> ListOpenAsync();
    Task<ApiResponse<object>> JoinAsync(Guid playerId, Guid tournamentId);
    Task<ApiResponse<object>> PlayRoundAsync(Guid playerId, Guid tournamentId);
    Task<object?> GetLeaderboardAsync(Guid tournamentId);
    Task UpdateScoreAsync(Guid tournamentId, Guid playerId, Guid sessionId, int solveSecs, int totalMoves);
}

public class TournamentService : ITournamentService
{
    readonly ISqlConnectionFactory _db;
    readonly ILevelService _level;

    public TournamentService(ISqlConnectionFactory db, ILevelService level)
    {
        _db = db;
        _level = level;
    }

    public async Task<object?> ListOpenAsync()
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_GetOpenTournaments", cn) { CommandType = CommandType.StoredProcedure };
        var list = new List<object>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            list.Add(new
            {
                TournamentId = rdr["TournamentId"].ToString(),
                Title = rdr["Title"].ToString(),
                EntryFeePaise = (long)rdr["EntryFeePaise"],
                PrizePoolPaise = (long)rdr["PrizePoolPaise"],
                MaxPlayers = (int)rdr["MaxPlayers"],
                CurrentPlayers = (int)rdr["CurrentPlayers"],
                TimeMode = rdr["TimeMode"].ToString(),
                Status = rdr["Status"].ToString(),
                StartsAt = (DateTime)rdr["StartsAt"],
                EndsAt = (DateTime)rdr["EndsAt"]
            });
        }
        return list;
    }

    public async Task<ApiResponse<object>> JoinAsync(Guid playerId, Guid tournamentId)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_JoinTournament", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        cmd.Parameters.AddWithValue("@TournamentId", tournamentId);
        var pRes = cmd.Parameters.Add("@Result", SqlDbType.Int);
        pRes.Direction = ParameterDirection.Output;
        var pMsg = cmd.Parameters.Add("@Message", SqlDbType.NVarChar, 200);
        pMsg.Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync();
        var result = (int)pRes.Value!;
        var msg = (string)pMsg.Value!;
        return result >= 1 ? new ApiResponse<object>(true, msg) : new ApiResponse<object>(false, msg);
    }

    public async Task<ApiResponse<object>> PlayRoundAsync(Guid playerId, Guid tournamentId)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();

        string? timeMode = null;
        long entryFee = 0;
        await using (var check = new SqlCommand(@"
            SELECT T.TimeMode, T.EntryFeePaise FROM TournamentEntries TE
            INNER JOIN Tournaments T ON T.TournamentId = TE.TournamentId
            WHERE TE.TournamentId = @Tid AND TE.PlayerId = @Pid AND T.Status = 'Open'", cn))
        {
            check.Parameters.AddWithValue("@Tid", tournamentId);
            check.Parameters.AddWithValue("@Pid", playerId);
            await using var rdr = await check.ExecuteReaderAsync();
            if (!await rdr.ReadAsync())
                return new ApiResponse<object>(false, "Join the tournament before playing");
            timeMode = rdr["TimeMode"].ToString();
            entryFee = (long)rdr["EntryFeePaise"];
        }

        var level = await _level.AllocateLevelAsync(playerId, new AllocateLevelRequest(timeMode!, "3x", entryFee));
        if (level == null) return new ApiResponse<object>(false, "Could not allocate level");

        var sessionId = Guid.NewGuid();
        var timeLimit = ParseTimeMode(timeMode!);
        await using var insert = new SqlCommand(@"
            INSERT INTO GameSessions (SessionId, Player1Id, GameMode, RewardMode, EntryFeePaise, RewardPaise,
                LevelId, TimeLimitSecs, Status, StartedAt, TournamentId)
            VALUES (@Sid, @Pid, 'Tournament', '3x', 0, 0, @LevelId, @Time, 'Active', GETDATE(), @Tid)", cn);
        insert.Parameters.AddWithValue("@Sid", sessionId);
        insert.Parameters.AddWithValue("@Pid", playerId);
        insert.Parameters.AddWithValue("@LevelId", level.LevelId);
        insert.Parameters.AddWithValue("@Time", timeLimit);
        insert.Parameters.AddWithValue("@Tid", tournamentId);
        await insert.ExecuteNonQueryAsync();

        return new ApiResponse<object>(true, "Tournament round started", new
        {
            SessionId = sessionId,
            level.LevelId,
            level.GridJson,
            level.PuzzleTier,
            level.TargetArrows,
            level.GridCols,
            level.GridRows,
            TimeLimitSecs = timeLimit,
            TournamentId = tournamentId
        });
    }

    public async Task<object?> GetLeaderboardAsync(Guid tournamentId)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_GetTournamentLeaderboard", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@TournamentId", tournamentId);
        var list = new List<object>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            list.Add(new
            {
                PlayerId = rdr["PlayerId"].ToString(),
                DisplayName = rdr["DisplayName"].ToString(),
                BestSolveSecs = rdr["BestSolveSecs"] == DBNull.Value ? (int?)null : (int)rdr["BestSolveSecs"],
                BestMoves = rdr["BestMoves"] == DBNull.Value ? (int?)null : (int)rdr["BestMoves"],
                Rank = (long)rdr["RankNum"]
            });
        }
        return list;
    }

    public async Task UpdateScoreAsync(Guid tournamentId, Guid playerId, Guid sessionId, int solveSecs, int totalMoves)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_UpdateTournamentScore", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@TournamentId", tournamentId);
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        cmd.Parameters.AddWithValue("@SessionId", sessionId);
        cmd.Parameters.AddWithValue("@SolveSecs", solveSecs);
        cmd.Parameters.AddWithValue("@TotalMoves", totalMoves);
        await cmd.ExecuteNonQueryAsync();
    }

    static int ParseTimeMode(string mode) => mode switch
    {
        "30sec" => 30,
        "2min" => 120,
        "3min" => 180,
        _ => 60
    };
}
