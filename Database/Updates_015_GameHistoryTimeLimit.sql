USE SpeedSagaDB;
GO
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE USP_GetPlayerGameHistory
    @PlayerId UNIQUEIDENTIFIER,
    @Page     INT = 1,
    @PageSize INT = 50
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Offset INT = (@Page - 1) * @PageSize;
    SELECT GS.SessionId, GS.GameMode, GS.RewardMode, GS.EntryFeePaise, GS.RewardPaise, GS.LevelId,
           GS.TimeLimitSecs, GS.Status, GS.StartedAt, GS.CompletedAt,
           CASE WHEN GS.WinnerId = @PlayerId THEN 1 ELSE 0 END AS IsWon,
           COALESCE(R.TotalMoves, SM.MoveCnt, 0) AS TotalMoves,
           COALESCE(R.SolvedInSecs, GS.Player1SolveSecs, 0) AS SolvedInSecs,
           CASE WHEN COALESCE(R.TotalMoves, SM.MoveCnt, 0) > 0 THEN 1 ELSE 0 END AS IsReplayAvailable,
           COALESCE(SM.MoveCnt, R.TotalMoves, 0) AS RecordedMoves
    FROM GameSessions GS
    OUTER APPLY (SELECT TOP 1 TotalMoves, SolvedInSecs FROM Replays WHERE SessionId = GS.SessionId AND PlayerId = @PlayerId) R
    OUTER APPLY (SELECT COUNT(*) AS MoveCnt FROM SessionMoves WHERE SessionId = GS.SessionId AND PlayerId = @PlayerId) SM
    WHERE GS.Player1Id = @PlayerId OR GS.Player2Id = @PlayerId
    ORDER BY GS.StartedAt DESC
    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
END
GO

PRINT 'Updates_015_GameHistoryTimeLimit applied.';
GO
