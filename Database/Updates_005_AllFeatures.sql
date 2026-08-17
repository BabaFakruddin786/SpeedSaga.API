-- SpeedSaga Updates 005: Tournaments, KYC, Profile, Matchmaking level, Dev deposit, 500 levels
USE SpeedSagaDB;
GO

-- ── Tournaments ──
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Tournaments')
CREATE TABLE Tournaments (
    TournamentId    UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    Title             NVARCHAR(200) NOT NULL,
    EntryFeePaise     BIGINT NOT NULL,
    PrizePoolPaise    BIGINT NOT NULL,
    MaxPlayers        INT NOT NULL DEFAULT 32,
    CurrentPlayers    INT NOT NULL DEFAULT 0,
    TimeMode          NVARCHAR(10) NOT NULL DEFAULT '1min',
    Status            NVARCHAR(20) NOT NULL DEFAULT 'Open',
    StartsAt          DATETIME NOT NULL DEFAULT GETDATE(),
    EndsAt            DATETIME NOT NULL DEFAULT DATEADD(DAY, 7, GETDATE())
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'TournamentEntries')
CREATE TABLE TournamentEntries (
    EntryId           UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    TournamentId      UNIQUEIDENTIFIER NOT NULL REFERENCES Tournaments(TournamentId),
    PlayerId          UNIQUEIDENTIFIER NOT NULL REFERENCES Players(PlayerId),
    JoinedAt          DATETIME NOT NULL DEFAULT GETDATE(),
    PlayerRank        INT NULL,
    RewardPaise       BIGINT NOT NULL DEFAULT 0,
    CONSTRAINT UQ_TournamentPlayer UNIQUE (TournamentId, PlayerId)
);
GO

-- Seed tournaments
IF NOT EXISTS (SELECT 1 FROM Tournaments)
INSERT INTO Tournaments (Title, EntryFeePaise, PrizePoolPaise, MaxPlayers, TimeMode, Status) VALUES
(N'Daily Blitz', 50000, 400000, 32, '1min', 'Open'),
(N'Weekend Championship', 100000, 1000000, 64, '2min', 'Open'),
(N'Mega Tournament', 250000, 5000000, 128, '3min', 'Open');
GO

-- ── Profile update ──
CREATE OR ALTER PROCEDURE USP_UpdatePlayerProfile
    @PlayerId   UNIQUEIDENTIFIER,
    @Username   NVARCHAR(50) = NULL,
    @StateCode  NVARCHAR(10) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE Players SET
        Username = COALESCE(NULLIF(LTRIM(RTRIM(@Username)), ''), Username),
        StateCode = COALESCE(NULLIF(LTRIM(RTRIM(@StateCode)), ''), StateCode)
    WHERE PlayerId = @PlayerId;
    SELECT @@ROWCOUNT AS RowsAffected;
END
GO

-- ── KYC submit ──
CREATE OR ALTER PROCEDURE USP_SubmitKyc
    @PlayerId   UNIQUEIDENTIFIER,
    @DocType    NVARCHAR(20),
    @DocNumber  NVARCHAR(100),
    @HolderName NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM PlayerKYC WHERE PlayerId = @PlayerId)
        INSERT INTO PlayerKYC (PlayerId) VALUES (@PlayerId);

    IF @DocType = 'Aadhaar'
        UPDATE PlayerKYC SET AadhaarStatus = 'Pending', AadhaarNumber = @DocNumber WHERE PlayerId = @PlayerId;
    ELSE IF @DocType = 'PAN'
        UPDATE PlayerKYC SET PANStatus = 'Pending', PANNumber = @DocNumber WHERE PlayerId = @PlayerId;
    ELSE IF @DocType = 'Bank'
        UPDATE PlayerKYC SET BankStatus = 'Pending', BankAccount = @DocNumber, BankName = @HolderName WHERE PlayerId = @PlayerId;

    UPDATE PlayerKYC SET IsFullyVerified = 0 WHERE PlayerId = @PlayerId;
END
GO

CREATE OR ALTER PROCEDURE USP_GetKycStatus
    @PlayerId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT AadhaarStatus, PANStatus, BankStatus, IsFullyVerified,
           LEFT(ISNULL(AadhaarNumber,''),4) AS AadhaarMasked,
           LEFT(ISNULL(PANNumber,''),4) AS PANMasked,
           LEFT(ISNULL(BankAccount,''),4) AS BankMasked
    FROM PlayerKYC WHERE PlayerId = @PlayerId;
END
GO

-- ── Dev wallet credit ──
CREATE OR ALTER PROCEDURE USP_DevCreditWallet
    @PlayerId   UNIQUEIDENTIFIER,
    @AmountPaise BIGINT,
    @Result     INT OUTPUT,
    @Message    NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @Result = 0; SET @Message = 'Failed';
    BEGIN TRY
        UPDATE Wallets SET BalancePaise = BalancePaise + @AmountPaise, DepositPaise = DepositPaise + @AmountPaise
        WHERE PlayerId = @PlayerId;
        INSERT INTO Transactions (TxnId, PlayerId, TxnType, AmountPaise, BalanceAfter, Status, GatewayRef, Remarks, CreatedAt)
        SELECT NEWID(), @PlayerId, 'Deposit', @AmountPaise, BalancePaise, 'Success', 'DEV-' + CAST(NEWID() AS NVARCHAR(50)), 'Dev test deposit', GETDATE()
        FROM Wallets WHERE PlayerId = @PlayerId;
        SET @Result = 1; SET @Message = 'Dev deposit credited';
    END TRY
    BEGIN CATCH
        SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO

-- ── Match status poll ──
CREATE OR ALTER PROCEDURE USP_GetMatchStatus
    @PlayerId   UNIQUEIDENTIFIER,
    @ConnId     NVARCHAR(200),
    @SessionId  UNIQUEIDENTIFIER OUTPUT,
    @IsMatched  BIT OUTPUT,
    @LevelId    INT OUTPUT,
    @GridJson   NVARCHAR(MAX) OUTPUT,
    @TimeLimit  INT OUTPUT,
    @RewardPaise BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @IsMatched = 0; SET @SessionId = NULL; SET @LevelId = NULL; SET @GridJson = NULL;
    SET @TimeLimit = NULL; SET @RewardPaise = NULL;

    SELECT TOP 1 @SessionId = MatchedSessionId
    FROM MatchmakingQueue
    WHERE PlayerId = @PlayerId AND SignalRConnId = @ConnId AND Status = 'Matched' AND MatchedSessionId IS NOT NULL
    ORDER BY MatchedAt DESC;

    IF @SessionId IS NULL RETURN;
    SET @IsMatched = 1;

    SELECT @LevelId = GS.LevelId, @TimeLimit = GS.TimeLimitSecs, @RewardPaise = GS.RewardPaise,
           @GridJson = L.GridJson
    FROM GameSessions GS
    LEFT JOIN Levels L ON L.LevelId = GS.LevelId
    WHERE GS.SessionId = @SessionId;
END
GO

-- ── Tournament SPs ──
CREATE OR ALTER PROCEDURE USP_GetOpenTournaments
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TournamentId, Title, EntryFeePaise, PrizePoolPaise, MaxPlayers, CurrentPlayers, TimeMode, Status, StartsAt, EndsAt
    FROM Tournaments WHERE Status = 'Open' AND EndsAt > GETDATE() ORDER BY StartsAt;
END
GO

CREATE OR ALTER PROCEDURE USP_JoinTournament
    @PlayerId       UNIQUEIDENTIFIER,
    @TournamentId   UNIQUEIDENTIFIER,
    @Result         INT OUTPUT,
    @Message        NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @Result = 0;
    DECLARE @Fee BIGINT, @MaxP INT, @CurP INT, @Status NVARCHAR(20);
    SELECT @Fee = EntryFeePaise, @MaxP = MaxPlayers, @CurP = CurrentPlayers, @Status = Status
    FROM Tournaments WHERE TournamentId = @TournamentId;

    IF @Status IS NULL BEGIN SET @Message = 'Tournament not found'; RETURN; END
    IF @Status <> 'Open' BEGIN SET @Message = 'Tournament not open'; RETURN; END
    IF @CurP >= @MaxP BEGIN SET @Message = 'Tournament full'; RETURN; END
    IF EXISTS (SELECT 1 FROM TournamentEntries WHERE TournamentId = @TournamentId AND PlayerId = @PlayerId)
    BEGIN SET @Message = 'Already joined'; SET @Result = 2; RETURN; END

    DECLARE @Bal BIGINT;
    SELECT @Bal = BalancePaise FROM Wallets WHERE PlayerId = @PlayerId;
    IF @Bal < @Fee BEGIN SET @Message = 'Insufficient balance'; RETURN; END

    BEGIN TRANSACTION;
    UPDATE Wallets SET BalancePaise = BalancePaise - @Fee WHERE PlayerId = @PlayerId AND BalancePaise >= @Fee;
    IF @@ROWCOUNT = 0 BEGIN ROLLBACK; SET @Message = 'Insufficient balance'; RETURN; END
    INSERT INTO TournamentEntries (TournamentId, PlayerId) VALUES (@TournamentId, @PlayerId);
    UPDATE Tournaments SET CurrentPlayers = CurrentPlayers + 1, PrizePoolPaise = PrizePoolPaise + @Fee WHERE TournamentId = @TournamentId;
    COMMIT;
    SET @Result = 1; SET @Message = 'Joined tournament';
END
GO

-- ── Generate levels up to 500 total ──
DECLARE @cnt INT = (SELECT COUNT(*) FROM Levels);
WHILE @cnt < 500
BEGIN
    DECLARE @lid INT = @cnt + 1;
        DECLARE @cols INT = 8 + (@lid % 7);
        DECLARE @rows INT = 8 + (@lid % 8);
        DECLARE @diff INT = 15 + (@lid % 85);
        DECLARE @tm NVARCHAR(10) = CASE (@lid % 5) WHEN 0 THEN '1min' WHEN 1 THEN '2min' WHEN 2 THEN '3min' WHEN 3 THEN '4min' ELSE '5min' END;
        DECLARE @arrowCount INT = 3 + (@lid % 12);
        DECLARE @json NVARCHAR(MAX) = '{"cols":' + CAST(@cols AS NVARCHAR) + ',"rows":' + CAST(@rows AS NVARCHAR) + ',"arrows":[';
        DECLARE @a INT = 1;
        WHILE @a <= @arrowCount
        BEGIN
            DECLARE @row INT = @a % @rows;
            DECLARE @len INT = 2 + (@a % 4);
            DECLARE @dir NVARCHAR(1) = CASE WHEN @a % 2 = 0 THEN 'R' ELSE 'L' END;
            DECLARE @pts NVARCHAR(500) = '[';
            DECLARE @p INT = 0;
            WHILE @p < @len
            BEGIN
                IF @p > 0 SET @pts = @pts + ',';
                SET @pts = @pts + '[' + CAST(CASE WHEN @dir='R' THEN @p ELSE (@len-1-@p) END AS NVARCHAR) + ',' + CAST(@row AS NVARCHAR) + ']';
                SET @p = @p + 1;
            END
            SET @pts = @pts + ']';
            IF @a > 1 SET @json = @json + ',';
            SET @json = @json + '{"id":' + CAST(@a AS NVARCHAR) + ',"pts":' + @pts + ',"dir":"' + @dir + '"}';
            SET @a = @a + 1;
        END
        SET @json = @json + ']}';
        INSERT INTO Levels (TimeMode, DifficultyScore, ArrowCount, GridCols, GridRows, Seed, GridJson, IsActive)
        VALUES (@tm, @diff, @arrowCount, @cols, @rows, 10000 + @lid, @json, 1);
    SET @cnt = @cnt + 1;
END
GO

PRINT 'Updates_005_AllFeatures applied.';
