-- SpeedSaga Updates 018: Exclude FreePlay from payment/game history and wallet stats.
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
    WHERE (GS.Player1Id = @PlayerId OR GS.Player2Id = @PlayerId)
      AND GS.GameMode <> 'FreePlay'
    ORDER BY GS.StartedAt DESC
    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
END
GO

CREATE OR ALTER PROCEDURE USP_GetTransactionHistory
    @PlayerId   UNIQUEIDENTIFIER,
    @TxnType    NVARCHAR(30)    = NULL,
    @PageNo     INT             = 1,
    @PageSize   INT             = 20
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Offset INT = (@PageNo - 1) * @PageSize;
    SELECT T.TxnId, T.TxnType, T.AmountPaise, T.BalanceAfter, T.Status, T.Gateway, T.GatewayRef, T.Remarks, T.CreatedAt
    FROM Transactions T
    WHERE T.PlayerId = @PlayerId
      AND (@TxnType IS NULL OR T.TxnType = @TxnType)
      AND NOT EXISTS (
          SELECT 1 FROM GameSessions GS
          WHERE GS.SessionId = T.SessionId AND GS.GameMode = 'FreePlay'
      )
    ORDER BY T.CreatedAt DESC
    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
END
GO

CREATE OR ALTER PROCEDURE USP_SubmitGameResult
    @SessionId      UNIQUEIDENTIFIER,
    @PlayerId       UNIQUEIDENTIFIER,
    @IsWon          BIT,
    @SolveSecs      INT,
    @MovesJson      NVARCHAR(MAX)   = NULL,
    @TotalMoves     INT             = 0,
    @Result         INT     OUTPUT,
    @Message        NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Mode NVARCHAR(30), @EntryFee BIGINT, @Reward BIGINT, @P1 UNIQUEIDENTIFIER, @P2 UNIQUEIDENTIFIER;
    SELECT @Mode = GameMode, @EntryFee = EntryFeePaise, @Reward = RewardPaise, @P1 = Player1Id, @P2 = Player2Id
    FROM GameSessions WHERE SessionId = @SessionId;

    IF @Mode IS NULL BEGIN SET @Result = -1; SET @Message = 'Session not found.'; RETURN; END

    BEGIN TRANSACTION;
    BEGIN TRY
        IF @MovesJson IS NOT NULL
            INSERT INTO Replays (SessionId, PlayerId, MovesJson, TotalMoves, SolvedInSecs)
            VALUES (@SessionId, @PlayerId, @MovesJson, @TotalMoves, CASE @IsWon WHEN 1 THEN @SolveSecs ELSE NULL END);

        IF @Mode = 'FreePlay'
        BEGIN
            UPDATE GameSessions
            SET Status = 'Complete',
                CompletedAt = GETDATE(),
                WinnerId = CASE WHEN @IsWon = 1 THEN @PlayerId ELSE WinnerId END,
                Player1SolveSecs = CASE WHEN Player1Id = @PlayerId THEN @SolveSecs ELSE Player1SolveSecs END,
                IsReplayAvailable = CASE WHEN @MovesJson IS NOT NULL THEN 1 ELSE IsReplayAvailable END
            WHERE SessionId = @SessionId;

            COMMIT;
            SET @Result = 1; SET @Message = 'Practice result saved.';
            RETURN;
        END

        IF @IsWon = 1
        BEGIN
            UPDATE Wallets
            SET BalancePaise += @Reward, WinningPaise += @Reward, UpdatedAt = GETDATE()
            WHERE PlayerId = @PlayerId;

            DECLARE @Bal BIGINT; SELECT @Bal = BalancePaise FROM Wallets WHERE PlayerId = @PlayerId;
            INSERT INTO Transactions (PlayerId, SessionId, TxnType, AmountPaise, BalanceAfter, Status, Remarks)
            VALUES (@PlayerId, @SessionId, 'Reward', @Reward, @Bal, 'Success', 'Game reward');

            UPDATE GameSessions SET WinnerId = @PlayerId, Status = 'Complete', CompletedAt = GETDATE(),
                Player1SolveSecs = CASE WHEN Player1Id = @PlayerId THEN @SolveSecs ELSE Player1SolveSecs END,
                Player2SolveSecs = CASE WHEN Player2Id = @PlayerId THEN @SolveSecs ELSE Player2SolveSecs END,
                IsReplayAvailable = 1
            WHERE SessionId = @SessionId;
        END
        ELSE
            UPDATE GameSessions
            SET Status = CASE WHEN Status = 'Active' THEN 'Complete' ELSE Status END,
                CompletedAt = ISNULL(CompletedAt, GETDATE())
            WHERE SessionId = @SessionId;

        UPDATE PlayerStats
        SET TotalGames  += 1,
            TotalWins   += CASE WHEN @IsWon = 1 THEN 1 ELSE 0 END,
            TotalLosses += CASE WHEN @IsWon = 0 THEN 1 ELSE 0 END,
            WinRatePct   = CAST((TotalWins + CASE WHEN @IsWon = 1 THEN 1 ELSE 0 END) * 100.0 / (TotalGames + 1) AS DECIMAL(5,2)),
            CurrentStreak = CASE WHEN @IsWon = 1 THEN CurrentStreak + 1 ELSE 0 END,
            BestStreak   = CASE WHEN @IsWon = 1 AND CurrentStreak + 1 > BestStreak THEN CurrentStreak + 1 ELSE BestStreak END,
            TotalEntryPaise += @EntryFee,
            TotalRewardPaise += CASE WHEN @IsWon = 1 THEN @Reward ELSE 0 END,
            LastGameAt  = GETDATE(),
            UpdatedAt   = GETDATE()
        WHERE PlayerId = @PlayerId;

        COMMIT;
        SET @Result = 1; SET @Message = 'Result submitted.';
    END TRY
    BEGIN CATCH
        ROLLBACK; SET @Result = -99; SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO

PRINT 'Updates_018_ExcludePracticeFromHistory applied.';
GO
