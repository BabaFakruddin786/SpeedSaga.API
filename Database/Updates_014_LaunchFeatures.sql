USE SpeedSagaDB;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- Referral code on dashboard
CREATE OR ALTER PROCEDURE USP_GetPlayerDashboard
    @PlayerId   UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT  P.PlayerId, P.ContactEmail, P.ContactPhone, P.Username, P.StateCode, P.ReferralCode,
            W.BalancePaise, W.DepositPaise, W.WinningPaise, W.WithdrawnPaise,
            S.TotalGames, S.TotalWins, S.TotalLosses, S.WinRatePct,
            S.CurrentStreak, S.BestStreak, S.TotalEntryPaise, S.TotalRewardPaise,
            K.AadhaarStatus, K.PANStatus, K.BankStatus, K.IsFullyVerified
    FROM Players P
    INNER JOIN Wallets W ON W.PlayerId = P.PlayerId
    INNER JOIN PlayerStats S ON S.PlayerId = P.PlayerId
    LEFT JOIN PlayerKYC K ON K.PlayerId = P.PlayerId
    WHERE P.PlayerId = @PlayerId;
END
GO

-- Promo claims
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'PlayerPromoClaims')
CREATE TABLE PlayerPromoClaims (
    ClaimId     UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    PlayerId    UNIQUEIDENTIFIER NOT NULL REFERENCES Players(PlayerId),
    PromoCode   NVARCHAR(40)     NOT NULL,
    BonusPaise  BIGINT           NOT NULL,
    ClaimedAt   DATETIME         NOT NULL DEFAULT GETDATE(),
    CONSTRAINT UQ_PlayerPromo UNIQUE (PlayerId, PromoCode)
);
GO

CREATE OR ALTER PROCEDURE USP_ClaimPromo
    @PlayerId   UNIQUEIDENTIFIER,
    @PromoCode  NVARCHAR(40),
    @BonusPaise BIGINT,
    @Result     INT OUTPUT,
    @Message    NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @Result = 0;
    IF EXISTS (SELECT 1 FROM PlayerPromoClaims WHERE PlayerId = @PlayerId AND PromoCode = @PromoCode)
    BEGIN SET @Message = 'Already claimed'; RETURN; END

    BEGIN TRANSACTION;
    INSERT INTO PlayerPromoClaims (PlayerId, PromoCode, BonusPaise) VALUES (@PlayerId, @PromoCode, @BonusPaise);
    UPDATE Wallets SET BalancePaise = BalancePaise + @BonusPaise, BonusPaise = BonusPaise + @BonusPaise WHERE PlayerId = @PlayerId;
    INSERT INTO Transactions (PlayerId, TxnType, AmountPaise, BalanceAfter, Status, Remarks)
    SELECT @PlayerId, 'Bonus', @BonusPaise, BalancePaise, 'Success', 'Promo: ' + @PromoCode FROM Wallets WHERE PlayerId = @PlayerId;
    COMMIT;
    SET @Result = 1; SET @Message = 'Bonus credited';
END
GO

-- Tournament round columns
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('TournamentEntries') AND name = 'BestSolveSecs')
    ALTER TABLE TournamentEntries ADD BestSolveSecs INT NULL, BestMoves INT NULL, RoundSessionId UNIQUEIDENTIFIER NULL;
GO

CREATE OR ALTER PROCEDURE USP_GetTournamentLeaderboard
    @TournamentId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TE.PlayerId, COALESCE(P.Username, P.ContactPhone, P.ContactEmail, 'Player') AS DisplayName,
           TE.BestSolveSecs, TE.BestMoves, TE.JoinedAt,
           ROW_NUMBER() OVER (ORDER BY CASE WHEN TE.BestSolveSecs IS NULL THEN 1 ELSE 0 END,
               TE.BestSolveSecs ASC, TE.BestMoves ASC, TE.JoinedAt ASC) AS RankNum
    FROM TournamentEntries TE
    INNER JOIN Players P ON P.PlayerId = TE.PlayerId
    WHERE TE.TournamentId = @TournamentId
    ORDER BY RankNum;
END
GO

CREATE OR ALTER PROCEDURE USP_UpdateTournamentScore
    @TournamentId UNIQUEIDENTIFIER,
    @PlayerId     UNIQUEIDENTIFIER,
    @SessionId    UNIQUEIDENTIFIER,
    @SolveSecs    INT,
    @TotalMoves   INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE TournamentEntries
    SET RoundSessionId = @SessionId,
        BestSolveSecs = CASE WHEN BestSolveSecs IS NULL OR @SolveSecs < BestSolveSecs THEN @SolveSecs ELSE BestSolveSecs END,
        BestMoves = CASE WHEN BestMoves IS NULL OR (@SolveSecs = BestSolveSecs AND @TotalMoves < BestMoves) THEN @TotalMoves
                    WHEN BestSolveSecs IS NULL OR @SolveSecs < BestSolveSecs THEN @TotalMoves ELSE BestMoves END
    WHERE TournamentId = @TournamentId AND PlayerId = @PlayerId;
END
GO

-- Two-player disconnect forfeit
CREATE OR ALTER PROCEDURE USP_ForfeitTwoPlayerSession
    @SessionId      UNIQUEIDENTIFIER,
    @ForfeitPlayerId UNIQUEIDENTIFIER,
    @Result         INT OUTPUT,
    @Message        NVARCHAR(200) OUTPUT,
    @WinnerId       UNIQUEIDENTIFIER OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @Result = 0; SET @WinnerId = NULL; SET @Message = 'Session not found';
    DECLARE @P1 UNIQUEIDENTIFIER, @P2 UNIQUEIDENTIFIER, @Status NVARCHAR(20), @Fee BIGINT, @Reward BIGINT;
    SELECT @P1 = Player1Id, @P2 = Player2Id, @Status = Status, @Fee = EntryFeePaise, @Reward = RewardPaise
    FROM GameSessions WHERE SessionId = @SessionId AND GameMode = 'TwoPlayer';

    IF @P1 IS NULL RETURN;
    IF @Status <> 'Active' BEGIN SET @Message = 'Session already ended'; SET @Result = -1; RETURN; END
    IF @ForfeitPlayerId NOT IN (@P1, @P2) BEGIN SET @Message = 'Not in session'; RETURN; END

    SET @WinnerId = CASE WHEN @ForfeitPlayerId = @P1 THEN @P2 ELSE @P1 END;

    BEGIN TRANSACTION;
    UPDATE GameSessions SET Status = 'Complete', WinnerId = @WinnerId, CompletedAt = GETDATE() WHERE SessionId = @SessionId;
    UPDATE Wallets SET BalancePaise = BalancePaise + @Reward, WinningPaise = WinningPaise + @Reward WHERE PlayerId = @WinnerId;
    INSERT INTO Transactions (PlayerId, TxnType, AmountPaise, BalanceAfter, Status, Remarks)
    SELECT @WinnerId, 'Winnings', @Reward, BalancePaise, 'Success', 'Opponent disconnected' FROM Wallets WHERE PlayerId = @WinnerId;
    COMMIT;
    SET @Result = 1; SET @Message = 'Opponent forfeited';
END
GO

CREATE OR ALTER PROCEDURE USP_CleanupStaleTwoPlayerSessions
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Sid UNIQUEIDENTIFIER, @P1 UNIQUEIDENTIFIER;
    DECLARE c CURSOR LOCAL FAST_FORWARD FOR
        SELECT SessionId, Player1Id FROM GameSessions
        WHERE GameMode = 'TwoPlayer' AND Status = 'Active'
          AND DATEDIFF(MINUTE, StartedAt, GETDATE()) > (TimeLimitSecs / 60) + 5;
    OPEN c;
    FETCH NEXT FROM c INTO @Sid, @P1;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        DECLARE @R INT, @M NVARCHAR(200), @W UNIQUEIDENTIFIER;
        EXEC USP_ForfeitTwoPlayerSession @Sid, @P1, @R OUTPUT, @M OUTPUT, @W OUTPUT;
        FETCH NEXT FROM c INTO @Sid, @P1;
    END
    CLOSE c; DEALLOCATE c;
END
GO

-- Replay available when moves exist (not only wins)
CREATE OR ALTER PROCEDURE USP_GetPlayerGameHistory
    @PlayerId UNIQUEIDENTIFIER,
    @Page     INT = 1,
    @PageSize INT = 50
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Offset INT = (@Page - 1) * @PageSize;
    SELECT GS.SessionId, GS.GameMode, GS.RewardMode, GS.EntryFeePaise, GS.RewardPaise, GS.LevelId,
           GS.Status, GS.StartedAt, GS.CompletedAt,
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

-- Tournament link on sessions
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('GameSessions') AND name = 'TournamentId')
    ALTER TABLE GameSessions ADD TournamentId UNIQUEIDENTIFIER NULL;
GO

PRINT 'Updates_014_LaunchFeatures applied.';
GO
