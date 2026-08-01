-- ============================================================
-- SPEEDSAGA — COMPLETE SQL SERVER DATABASE SCRIPT
-- All Tables, Indexes, Stored Procedures, Seed Data
-- Compatible: SQL Server 2019+
-- ============================================================

USE master;
GO

IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'SpeedSagaDB')
    CREATE DATABASE SpeedSagaDB;
GO

USE SpeedSagaDB;
GO

-- ============================================================
-- SECTION 1: TABLES
-- ============================================================

-- 1.1 Players
CREATE TABLE Players (
    PlayerId        UNIQUEIDENTIFIER    DEFAULT NEWID()     PRIMARY KEY,
    ContactEmail    NVARCHAR(150)       NULL,
    ContactPhone    NVARCHAR(15)        NULL,
    PasswordHash    NVARCHAR(512)       NOT NULL,
    PasswordSalt    NVARCHAR(256)       NOT NULL,
    Username        NVARCHAR(50)        NULL,
    ReferralCode    NVARCHAR(20)        NULL,
    ReferredBy      UNIQUEIDENTIFIER    NULL,
    IsAgeVerified   BIT                 DEFAULT 0,
    IsTermsAccepted BIT                 DEFAULT 0,
    IsActive        BIT                 DEFAULT 1,
    IsBanned        BIT                 DEFAULT 0,
    BannedReason    NVARCHAR(500)       NULL,
    StateCode       NVARCHAR(10)        NULL,         -- For geoblocking
    CreatedAt       DATETIME            DEFAULT GETDATE(),
    LastLoginAt     DATETIME            NULL,
    CONSTRAINT UQ_Player_Email   UNIQUE (ContactEmail),
    CONSTRAINT UQ_Player_Phone   UNIQUE (ContactPhone),
    CONSTRAINT UQ_Player_Referral UNIQUE (ReferralCode)
);
GO

-- 1.2 Wallet
CREATE TABLE Wallets (
    WalletId        UNIQUEIDENTIFIER    DEFAULT NEWID()     PRIMARY KEY,
    PlayerId        UNIQUEIDENTIFIER    NOT NULL REFERENCES Players(PlayerId),
    BalancePaise    BIGINT              DEFAULT 0,     -- Store in paise (100 paise = 1 Rs)
    DepositPaise    BIGINT              DEFAULT 0,     -- Total deposited ever
    WinningPaise    BIGINT              DEFAULT 0,     -- Total won ever
    WithdrawnPaise  BIGINT              DEFAULT 0,     -- Total withdrawn ever
    BonusPaise      BIGINT              DEFAULT 0,     -- Bonus/referral credits
    UpdatedAt       DATETIME            DEFAULT GETDATE(),
    CONSTRAINT UQ_Wallet_Player UNIQUE (PlayerId)
);
GO

-- 1.3 PlayerStats (for Level Allocation Engine)
CREATE TABLE PlayerStats (
    PlayerId            UNIQUEIDENTIFIER    PRIMARY KEY REFERENCES Players(PlayerId),
    TotalGames          INT                 DEFAULT 0,
    TotalWins           INT                 DEFAULT 0,
    TotalLosses         INT                 DEFAULT 0,
    TotalTimeouts       INT                 DEFAULT 0,
    WinRatePct          DECIMAL(5,2)        DEFAULT 0.00,   -- 0.00 to 100.00
    AvgSolveTimeSecs    DECIMAL(8,2)        DEFAULT 0,
    CurrentStreak       INT                 DEFAULT 0,
    BestStreak          INT                 DEFAULT 0,
    TotalEntryPaise     BIGINT              DEFAULT 0,
    TotalRewardPaise    BIGINT              DEFAULT 0,
    LastGameAt          DATETIME            NULL,
    UpdatedAt           DATETIME            DEFAULT GETDATE()
);
GO

-- 1.4 KYC Verification
CREATE TABLE PlayerKYC (
    KYCId           UNIQUEIDENTIFIER    DEFAULT NEWID()     PRIMARY KEY,
    PlayerId        UNIQUEIDENTIFIER    NOT NULL REFERENCES Players(PlayerId),
    AadhaarStatus   NVARCHAR(20)        DEFAULT 'NotSubmitted',  -- NotSubmitted|Pending|Verified|Rejected
    PANStatus       NVARCHAR(20)        DEFAULT 'NotSubmitted',
    BankStatus      NVARCHAR(20)        DEFAULT 'NotSubmitted',
    AadhaarNumber   NVARCHAR(20)        NULL,   -- Masked: XXXX-XXXX-1234
    PANNumber       NVARCHAR(15)        NULL,
    BankAccount     NVARCHAR(30)        NULL,
    BankIFSC        NVARCHAR(15)        NULL,
    BankName        NVARCHAR(100)       NULL,
    IsFullyVerified BIT                 DEFAULT 0,
    UpdatedAt       DATETIME            DEFAULT GETDATE(),
    CONSTRAINT UQ_KYC_Player UNIQUE (PlayerId)
);
GO

-- 1.5 Levels
CREATE TABLE Levels (
    LevelId         INT IDENTITY(1,1)   PRIMARY KEY,
    TimeMode        NVARCHAR(10)        NOT NULL,   -- '1min','2min','3min','4min','5min'
    DifficultyScore INT                 NOT NULL,   -- 1-100
    GridJson        NVARCHAR(MAX)       NOT NULL,   -- Full puzzle JSON
    ArrowCount      INT                 NOT NULL,   -- Total arrows in puzzle
    GridCols        INT                 NOT NULL,
    GridRows        INT                 NOT NULL,
    Seed            INT                 NOT NULL,   -- For shuffle verification
    IsActive        BIT                 DEFAULT 1,
    CreatedAt       DATETIME            DEFAULT GETDATE()
);
GO

-- 1.6 Game Sessions
CREATE TABLE GameSessions (
    SessionId           UNIQUEIDENTIFIER    DEFAULT NEWID()     PRIMARY KEY,
    Player1Id           UNIQUEIDENTIFIER    NOT NULL REFERENCES Players(PlayerId),
    Player2Id           UNIQUEIDENTIFIER    NULL REFERENCES Players(PlayerId),
    GameMode            NVARCHAR(30)        NOT NULL,   -- 'FreePlay','SinglePlayer3x','SinglePlayer5x','TwoPlayer'
    RewardMode          NVARCHAR(10)        NULL,       -- '3x','5x','85pct'
    EntryFeePaise       BIGINT              DEFAULT 0,
    RewardPaise         BIGINT              DEFAULT 0,
    LevelId             INT                 NULL REFERENCES Levels(LevelId),
    TimeLimitSecs       INT                 NOT NULL,
    Player1SolveSecs    INT                 NULL,       -- How fast P1 solved
    Player2SolveSecs    INT                 NULL,
    WinnerId            UNIQUEIDENTIFIER    NULL REFERENCES Players(PlayerId),
    Status              NVARCHAR(20)        DEFAULT 'Waiting',  -- Waiting|Active|Complete|Timeout|Void|Cancelled
    StartedAt           DATETIME            NULL,
    CompletedAt         DATETIME            NULL,
    CreatedAt           DATETIME            DEFAULT GETDATE(),
    SignalRGroupId      NVARCHAR(100)       NULL,       -- For real-time hub grouping
    IsReplayAvailable   BIT                 DEFAULT 0
);
GO

-- 1.7 Replays
CREATE TABLE Replays (
    ReplayId        UNIQUEIDENTIFIER    DEFAULT NEWID()     PRIMARY KEY,
    SessionId       UNIQUEIDENTIFIER    NOT NULL REFERENCES GameSessions(SessionId),
    PlayerId        UNIQUEIDENTIFIER    NOT NULL REFERENCES Players(PlayerId),
    MovesJson       NVARCHAR(MAX)       NOT NULL,   -- [{dir,col,row,timestamp}]
    TotalMoves      INT                 DEFAULT 0,
    SolvedInSecs    INT                 NULL,
    CreatedAt       DATETIME            DEFAULT GETDATE()
);
GO

-- 1.8 Transactions
CREATE TABLE Transactions (
    TxnId           UNIQUEIDENTIFIER    DEFAULT NEWID()     PRIMARY KEY,
    PlayerId        UNIQUEIDENTIFIER    NOT NULL REFERENCES Players(PlayerId),
    SessionId       UNIQUEIDENTIFIER    NULL REFERENCES GameSessions(SessionId),
    TxnType         NVARCHAR(30)        NOT NULL,   -- 'Deposit','Withdrawal','EntryFee','Reward','Refund','Bonus'
    AmountPaise     BIGINT              NOT NULL,
    BalanceAfter    BIGINT              NOT NULL,
    Status          NVARCHAR(20)        DEFAULT 'Pending',  -- Pending|Success|Failed|Reversed
    Gateway         NVARCHAR(30)        NULL,       -- 'Razorpay','Manual'
    GatewayRef      NVARCHAR(200)       NULL,       -- Razorpay order/payment ID
    GatewayOrderId  NVARCHAR(200)       NULL,
    Remarks         NVARCHAR(500)       NULL,
    CreatedAt       DATETIME            DEFAULT GETDATE(),
    UpdatedAt       DATETIME            DEFAULT GETDATE()
);
GO

-- 1.9 Matchmaking Queue
CREATE TABLE MatchmakingQueue (
    QueueId         UNIQUEIDENTIFIER    DEFAULT NEWID()     PRIMARY KEY,
    PlayerId        UNIQUEIDENTIFIER    NOT NULL REFERENCES Players(PlayerId),
    EntryFeePaise   BIGINT              NOT NULL,
    TimeLimitSecs   INT                 NOT NULL,
    SignalRConnId   NVARCHAR(200)       NULL,
    Status          NVARCHAR(20)        DEFAULT 'Waiting',  -- Waiting|Matched|Cancelled|Expired
    JoinedAt        DATETIME            DEFAULT GETDATE(),
    MatchedAt       DATETIME            NULL,
    MatchedSessionId UNIQUEIDENTIFIER   NULL REFERENCES GameSessions(SessionId)
);
GO

-- 1.10 Bot Detection Log
CREATE TABLE BotFlagLog (
    FlagId          UNIQUEIDENTIFIER    DEFAULT NEWID()     PRIMARY KEY,
    PlayerId        UNIQUEIDENTIFIER    NOT NULL REFERENCES Players(PlayerId),
    SessionId       UNIQUEIDENTIFIER    NULL,
    FlagReason      NVARCHAR(200)       NOT NULL,
    FlagScore       INT                 DEFAULT 0,      -- 0-100 suspicion score
    IsReviewed      BIT                 DEFAULT 0,
    ReviewedBy      NVARCHAR(100)       NULL,
    ReviewAction    NVARCHAR(50)        NULL,           -- 'Cleared','Warned','Banned'
    CreatedAt       DATETIME            DEFAULT GETDATE()
);
GO

-- 1.11 Notifications
CREATE TABLE Notifications (
    NotifId         UNIQUEIDENTIFIER    DEFAULT NEWID()     PRIMARY KEY,
    PlayerId        UNIQUEIDENTIFIER    NULL REFERENCES Players(PlayerId), -- NULL = broadcast
    Title           NVARCHAR(200)       NOT NULL,
    Body            NVARCHAR(1000)      NOT NULL,
    NotifType       NVARCHAR(50)        NOT NULL,   -- 'System','Win','Deposit','Promo'
    IsRead          BIT                 DEFAULT 0,
    FCMSent         BIT                 DEFAULT 0,
    CreatedAt       DATETIME            DEFAULT GETDATE()
);
GO

-- 1.12 Restricted States (Geoblocking)
CREATE TABLE RestrictedStates (
    StateCode       NVARCHAR(10)        PRIMARY KEY,
    StateName       NVARCHAR(100)       NOT NULL,
    Reason          NVARCHAR(300)       NULL,
    IsActive        BIT                 DEFAULT 1
);
GO

-- ============================================================
-- SECTION 2: INDEXES
-- ============================================================

CREATE INDEX IX_Players_Email       ON Players(ContactEmail);
CREATE INDEX IX_Players_Phone       ON Players(ContactPhone);
CREATE INDEX IX_Wallets_Player      ON Wallets(PlayerId);
CREATE INDEX IX_Stats_WinRate       ON PlayerStats(WinRatePct);
CREATE INDEX IX_Sessions_P1         ON GameSessions(Player1Id, Status);
CREATE INDEX IX_Sessions_Status     ON GameSessions(Status, CreatedAt);
CREATE INDEX IX_Txn_Player          ON Transactions(PlayerId, CreatedAt DESC);
CREATE INDEX IX_Txn_Gateway         ON Transactions(GatewayRef) WHERE GatewayRef IS NOT NULL;
CREATE INDEX IX_Queue_Fee           ON MatchmakingQueue(EntryFeePaise, Status, JoinedAt);
CREATE INDEX IX_Levels_TimeMode     ON Levels(TimeMode, DifficultyScore) WHERE IsActive = 1;
CREATE INDEX IX_Bot_Player          ON BotFlagLog(PlayerId, CreatedAt DESC);
GO

-- ============================================================
-- SECTION 3: STORED PROCEDURES
-- ============================================================

-- 3.1 Register Player
CREATE OR ALTER PROCEDURE USP_RegisterPlayer
    @ContactEmail   NVARCHAR(150)   = NULL,
    @ContactPhone   NVARCHAR(15)    = NULL,
    @PasswordHash   NVARCHAR(512),
    @PasswordSalt   NVARCHAR(256),
    @ReferralCode   NVARCHAR(20)    = NULL,
    @StateCode      NVARCHAR(10)    = NULL,
    @NewPlayerId    UNIQUEIDENTIFIER OUTPUT,
    @Result         INT             OUTPUT,     -- 1=Success, -1=Email exists, -2=Phone exists, -3=Restricted state
    @Message        NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    -- Check restricted state
    IF @StateCode IS NOT NULL AND EXISTS (SELECT 1 FROM RestrictedStates WHERE StateCode = @StateCode AND IsActive = 1)
    BEGIN
        SET @Result = -3; SET @Message = 'Real-money gaming is not available in your state.'; RETURN;
    END

    IF @ContactEmail IS NOT NULL AND EXISTS (SELECT 1 FROM Players WHERE ContactEmail = @ContactEmail)
    BEGIN
        SET @Result = -1; SET @Message = 'Email already registered.'; RETURN;
    END

    IF @ContactPhone IS NOT NULL AND EXISTS (SELECT 1 FROM Players WHERE ContactPhone = @ContactPhone)
    BEGIN
        SET @Result = -2; SET @Message = 'Phone number already registered.'; RETURN;
    END

    -- Generate unique referral code
    DECLARE @MyReferral NVARCHAR(20) = UPPER(LEFT(REPLACE(CAST(NEWID() AS NVARCHAR(36)),'-',''), 8));

    -- Find referrer
    DECLARE @ReferrerId UNIQUEIDENTIFIER = NULL;
    IF @ReferralCode IS NOT NULL
        SELECT @ReferrerId = PlayerId FROM Players WHERE ReferralCode = @ReferralCode;

    BEGIN TRANSACTION;
    BEGIN TRY
        SET @NewPlayerId = NEWID();

        INSERT INTO Players (PlayerId, ContactEmail, ContactPhone, PasswordHash, PasswordSalt, ReferralCode, ReferredBy, StateCode)
        VALUES (@NewPlayerId, @ContactEmail, @ContactPhone, @PasswordHash, @PasswordSalt, @MyReferral, @ReferrerId, @StateCode);

        INSERT INTO Wallets (PlayerId) VALUES (@NewPlayerId);
        INSERT INTO PlayerStats (PlayerId) VALUES (@NewPlayerId);
        INSERT INTO PlayerKYC (PlayerId) VALUES (@NewPlayerId);

        -- Referral bonus (100 Rs = 10000 paise)
        IF @ReferrerId IS NOT NULL
            UPDATE Wallets SET BalancePaise += 10000, BonusPaise += 10000 WHERE PlayerId = @ReferrerId;

        COMMIT;
        SET @Result = 1; SET @Message = 'Registration successful.';
    END TRY
    BEGIN CATCH
        ROLLBACK;
        SET @Result = -99; SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO

-- 3.2 Login
CREATE OR ALTER PROCEDURE USP_LoginPlayer
    @Contact        NVARCHAR(150),
    @PlayerId       UNIQUEIDENTIFIER    OUTPUT,
    @PasswordHash   NVARCHAR(512)       OUTPUT,
    @PasswordSalt   NVARCHAR(256)       OUTPUT,
    @StateCode      NVARCHAR(10)        OUTPUT,
    @Result         INT                 OUTPUT   -- 1=Found, 0=Not found, -1=Banned
AS
BEGIN
    SET NOCOUNT ON;
    SELECT  @PlayerId       = PlayerId,
            @PasswordHash   = PasswordHash,
            @PasswordSalt   = PasswordSalt,
            @StateCode      = StateCode,
            @Result         = CASE WHEN IsBanned = 1 THEN -1 ELSE 1 END
    FROM Players
    WHERE (ContactEmail = @Contact OR ContactPhone = @Contact) AND IsActive = 1;

    IF @PlayerId IS NOT NULL
        UPDATE Players SET LastLoginAt = GETDATE() WHERE PlayerId = @PlayerId;
    ELSE
        SET @Result = 0;
END
GO

-- 3.3 Get Wallet Balance
CREATE OR ALTER PROCEDURE USP_GetWallet
    @PlayerId   UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT  W.WalletId, W.BalancePaise, W.DepositPaise, W.WinningPaise,
            W.WithdrawnPaise, W.BonusPaise,
            K.AadhaarStatus, K.PANStatus, K.BankStatus, K.IsFullyVerified
    FROM Wallets W
    LEFT JOIN PlayerKYC K ON K.PlayerId = W.PlayerId
    WHERE W.PlayerId = @PlayerId;
END
GO

-- 3.4 Deduct Entry Fee (Atomic)
CREATE OR ALTER PROCEDURE USP_DeductEntryFee
    @PlayerId       UNIQUEIDENTIFIER,
    @SessionId      UNIQUEIDENTIFIER,
    @FeePaise       BIGINT,
    @Result         INT     OUTPUT,     -- 1=OK, -1=Insufficient, -2=Session invalid
    @Message        NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;
    BEGIN TRY
        DECLARE @Balance BIGINT;
        SELECT @Balance = BalancePaise FROM Wallets WITH (UPDLOCK, ROWLOCK) WHERE PlayerId = @PlayerId;

        IF @Balance < @FeePaise
        BEGIN
            ROLLBACK; SET @Result = -1; SET @Message = 'Insufficient balance.'; RETURN;
        END

        UPDATE Wallets SET BalancePaise -= @FeePaise, UpdatedAt = GETDATE() WHERE PlayerId = @PlayerId;

        DECLARE @NewBalance BIGINT = @Balance - @FeePaise;
        INSERT INTO Transactions (PlayerId, SessionId, TxnType, AmountPaise, BalanceAfter, Status, Remarks)
        VALUES (@PlayerId, @SessionId, 'EntryFee', @FeePaise, @NewBalance, 'Success', 'Game entry fee deducted');

        COMMIT;
        SET @Result = 1; SET @Message = 'Entry fee deducted.';
    END TRY
    BEGIN CATCH
        ROLLBACK; SET @Result = -99; SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO

-- 3.5 Credit Reward to Winner
CREATE OR ALTER PROCEDURE USP_CreditReward
    @PlayerId       UNIQUEIDENTIFIER,
    @SessionId      UNIQUEIDENTIFIER,
    @RewardPaise    BIGINT,
    @Result         INT     OUTPUT,
    @Message        NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;
    BEGIN TRY
        DECLARE @Balance BIGINT;
        SELECT @Balance = BalancePaise FROM Wallets WITH (UPDLOCK, ROWLOCK) WHERE PlayerId = @PlayerId;

        UPDATE Wallets
        SET BalancePaise    += @RewardPaise,
            WinningPaise    += @RewardPaise,
            UpdatedAt        = GETDATE()
        WHERE PlayerId = @PlayerId;

        DECLARE @NewBalance BIGINT = @Balance + @RewardPaise;
        INSERT INTO Transactions (PlayerId, SessionId, TxnType, AmountPaise, BalanceAfter, Status, Remarks)
        VALUES (@PlayerId, @SessionId, 'Reward', @RewardPaise, @NewBalance, 'Success', 'Game reward credited');

        UPDATE GameSessions SET WinnerId = @PlayerId, Status = 'Complete', CompletedAt = GETDATE()
        WHERE SessionId = @SessionId;

        COMMIT;
        SET @Result = 1; SET @Message = 'Reward credited.';
    END TRY
    BEGIN CATCH
        ROLLBACK; SET @Result = -99; SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO

-- 3.6 Razorpay Deposit
CREATE OR ALTER PROCEDURE USP_ProcessDeposit
    @PlayerId           UNIQUEIDENTIFIER,
    @AmountPaise        BIGINT,
    @RazorpayOrderId    NVARCHAR(200),
    @RazorpayPaymentId  NVARCHAR(200),
    @Result             INT     OUTPUT,
    @Message            NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    -- Idempotency check
    IF EXISTS (SELECT 1 FROM Transactions WHERE GatewayRef = @RazorpayPaymentId AND Status = 'Success')
    BEGIN
        SET @Result = -1; SET @Message = 'Duplicate payment reference.'; RETURN;
    END

    BEGIN TRANSACTION;
    BEGIN TRY
        DECLARE @Balance BIGINT;
        SELECT @Balance = BalancePaise FROM Wallets WITH (UPDLOCK, ROWLOCK) WHERE PlayerId = @PlayerId;

        UPDATE Wallets
        SET BalancePaise    += @AmountPaise,
            DepositPaise    += @AmountPaise,
            UpdatedAt        = GETDATE()
        WHERE PlayerId = @PlayerId;

        DECLARE @NewBalance BIGINT = @Balance + @AmountPaise;
        INSERT INTO Transactions (PlayerId, TxnType, AmountPaise, BalanceAfter, Status, Gateway, GatewayRef, GatewayOrderId, Remarks)
        VALUES (@PlayerId, 'Deposit', @AmountPaise, @NewBalance, 'Success', 'Razorpay', @RazorpayPaymentId, @RazorpayOrderId, 'Wallet deposit via Razorpay');

        COMMIT;
        SET @Result = 1; SET @Message = 'Deposit successful.';
    END TRY
    BEGIN CATCH
        ROLLBACK; SET @Result = -99; SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO

-- 3.7 Allocate Level (Win Rate Engine)
CREATE OR ALTER PROCEDURE USP_AllocateLevel
    @PlayerId       UNIQUEIDENTIFIER,
    @TimeMode       NVARCHAR(10),
    @RewardMode     NVARCHAR(10),   -- '3x' or '5x'
    @LevelId        INT     OUTPUT,
    @GridJson       NVARCHAR(MAX) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @WinRate DECIMAL(5,2) = 0;
    SELECT @WinRate = WinRatePct FROM PlayerStats WHERE PlayerId = @PlayerId;

    -- Target win rates per requirements
    DECLARE @TargetWin DECIMAL(5,2) = CASE @RewardMode WHEN '3x' THEN 20.00 WHEN '5x' THEN 10.00 ELSE 20.00 END;

    -- Difficulty bracket
    DECLARE @MinDiff INT, @MaxDiff INT;

    IF @WinRate > @TargetWin + 10       -- Much higher than target → very hard
        SELECT @MinDiff = 80, @MaxDiff = 100;
    ELSE IF @WinRate > @TargetWin + 5  -- Slightly above target → hard
        SELECT @MinDiff = 60, @MaxDiff = 85;
    ELSE IF @WinRate > @TargetWin       -- At/near target → medium-hard
        SELECT @MinDiff = 45, @MaxDiff = 70;
    ELSE IF @WinRate < @TargetWin - 15  -- Much below (retention) → easier
        SELECT @MinDiff = 15, @MaxDiff = 40;
    ELSE                                 -- Normal → medium
        SELECT @MinDiff = 30, @MaxDiff = 60;

    -- Random level in bracket
    SELECT TOP 1 @LevelId = LevelId, @GridJson = GridJson
    FROM Levels
    WHERE TimeMode = @TimeMode
      AND DifficultyScore BETWEEN @MinDiff AND @MaxDiff
      AND IsActive = 1
    ORDER BY NEWID();   -- Random shuffle

    -- Fallback to any level if bracket is empty
    IF @LevelId IS NULL
        SELECT TOP 1 @LevelId = LevelId, @GridJson = GridJson
        FROM Levels WHERE TimeMode = @TimeMode AND IsActive = 1
        ORDER BY NEWID();
END
GO

-- 3.8 Submit Game Result
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
        -- Store replay
        IF @MovesJson IS NOT NULL
            INSERT INTO Replays (SessionId, PlayerId, MovesJson, TotalMoves, SolvedInSecs)
            VALUES (@SessionId, @PlayerId, @MovesJson, @TotalMoves, CASE @IsWon WHEN 1 THEN @SolveSecs ELSE NULL END);

        IF @IsWon = 1
        BEGIN
            -- Credit reward
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

        -- Update player stats
        UPDATE PlayerStats
        SET TotalGames  += 1,
            TotalWins   += CASE WHEN @IsWon = 1 THEN 1 ELSE 0 END,
            TotalLosses += CASE WHEN @IsWon = 0 THEN 1 ELSE 0 END,
            WinRatePct   = CAST(TotalWins * 100.0 / (TotalGames + 1) AS DECIMAL(5,2)),
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

-- 3.9 Matchmaking — Find or Create Session
CREATE OR ALTER PROCEDURE USP_MatchmakingJoin
    @PlayerId       UNIQUEIDENTIFIER,
    @FeePaise       BIGINT,
    @TimeSecs       INT,
    @ConnId         NVARCHAR(200),
    @SessionId      UNIQUEIDENTIFIER    OUTPUT,
    @IsNewSession   BIT                 OUTPUT,
    @OpponentId     UNIQUEIDENTIFIER    OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @IsNewSession = 0; SET @OpponentId = NULL;

    BEGIN TRANSACTION;
    BEGIN TRY
        -- Look for waiting opponent with same fee and time
        DECLARE @WaitId UNIQUEIDENTIFIER, @WaitPlayerId UNIQUEIDENTIFIER;
        SELECT TOP 1 @WaitId = QueueId, @WaitPlayerId = PlayerId
        FROM MatchmakingQueue WITH (UPDLOCK, ROWLOCK)
        WHERE EntryFeePaise = @FeePaise AND TimeLimitSecs = @TimeSecs
          AND Status = 'Waiting' AND PlayerId <> @PlayerId
          AND DATEDIFF(SECOND, JoinedAt, GETDATE()) < 60
        ORDER BY JoinedAt;

        IF @WaitPlayerId IS NOT NULL
        BEGIN
            -- Match found — create session
            SET @SessionId = NEWID();
            SET @OpponentId = @WaitPlayerId;

            INSERT INTO GameSessions (SessionId, Player1Id, Player2Id, GameMode, EntryFeePaise,
                RewardPaise, TimeLimitSecs, Status, StartedAt, SignalRGroupId)
            VALUES (@SessionId, @WaitPlayerId, @PlayerId, 'TwoPlayer',
                @FeePaise, CAST(@FeePaise * 2 * 0.85 AS BIGINT), @TimeSecs,
                'Active', GETDATE(), CAST(@SessionId AS NVARCHAR(50)));

            UPDATE MatchmakingQueue
            SET Status = 'Matched', MatchedAt = GETDATE(), MatchedSessionId = @SessionId
            WHERE QueueId = @WaitId;

            -- Add current player to matched state
            INSERT INTO MatchmakingQueue (PlayerId, EntryFeePaise, TimeLimitSecs, SignalRConnId, Status, MatchedAt, MatchedSessionId)
            VALUES (@PlayerId, @FeePaise, @TimeSecs, @ConnId, 'Matched', GETDATE(), @SessionId);
        END
        ELSE
        BEGIN
            -- No match — add to queue
            SET @IsNewSession = 1;
            INSERT INTO MatchmakingQueue (PlayerId, EntryFeePaise, TimeLimitSecs, SignalRConnId, Status)
            VALUES (@PlayerId, @FeePaise, @TimeSecs, @ConnId, 'Waiting');
        END

        COMMIT;
    END TRY
    BEGIN CATCH
        ROLLBACK; THROW;
    END CATCH
END
GO

-- 3.10 Get Player Dashboard
CREATE OR ALTER PROCEDURE USP_GetPlayerDashboard
    @PlayerId   UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT  P.PlayerId, P.ContactEmail, P.ContactPhone, P.Username, P.StateCode,
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

-- 3.11 Get Transaction History
CREATE OR ALTER PROCEDURE USP_GetTransactionHistory
    @PlayerId   UNIQUEIDENTIFIER,
    @TxnType    NVARCHAR(30)    = NULL,
    @PageNo     INT             = 1,
    @PageSize   INT             = 20
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Offset INT = (@PageNo - 1) * @PageSize;
    SELECT TxnId, TxnType, AmountPaise, BalanceAfter, Status, Gateway, GatewayRef, Remarks, CreatedAt
    FROM Transactions
    WHERE PlayerId = @PlayerId
      AND (@TxnType IS NULL OR TxnType = @TxnType)
    ORDER BY CreatedAt DESC
    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
END
GO

-- 3.12 Recalculate Win Rates (Hangfire Job)
CREATE OR ALTER PROCEDURE USP_RecalculateWinRates
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE PlayerStats
    SET WinRatePct = CASE WHEN TotalGames = 0 THEN 0
                          ELSE CAST(TotalWins * 100.0 / TotalGames AS DECIMAL(5,2)) END,
        UpdatedAt  = GETDATE();
    PRINT 'Win rates recalculated for ' + CAST(@@ROWCOUNT AS NVARCHAR) + ' players.';
END
GO

-- 3.13 Bot Detection Flag
CREATE OR ALTER PROCEDURE USP_FlagSuspiciousPlayer
    @PlayerId       UNIQUEIDENTIFIER,
    @SessionId      UNIQUEIDENTIFIER    = NULL,
    @FlagReason     NVARCHAR(200),
    @FlagScore      INT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO BotFlagLog (PlayerId, SessionId, FlagReason, FlagScore)
    VALUES (@PlayerId, @SessionId, @FlagReason, @FlagScore);

    -- Auto-ban if cumulative score > 200
    DECLARE @TotalScore INT;
    SELECT @TotalScore = SUM(FlagScore)
    FROM BotFlagLog WHERE PlayerId = @PlayerId AND CreatedAt > DATEADD(DAY, -7, GETDATE());

    IF @TotalScore > 200
        UPDATE Players SET IsBanned = 1, BannedReason = 'Auto-ban: High bot suspicion score' WHERE PlayerId = @PlayerId;
END
GO

-- 3.14 Cleanup Expired Queue
CREATE OR ALTER PROCEDURE USP_CleanupExpiredQueue
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE MatchmakingQueue
    SET Status = 'Expired'
    WHERE Status = 'Waiting' AND DATEDIFF(SECOND, JoinedAt, GETDATE()) > 60;
    PRINT 'Expired queue entries: ' + CAST(@@ROWCOUNT AS NVARCHAR);
END
GO

-- 3.15 Save/Get Replay
CREATE OR ALTER PROCEDURE USP_GetReplay
    @SessionId  UNIQUEIDENTIFIER,
    @PlayerId   UNIQUEIDENTIFIER    = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT R.ReplayId, R.PlayerId, R.MovesJson, R.TotalMoves, R.SolvedInSecs, R.CreatedAt,
           G.LevelId, G.TimeLimitSecs, G.GameMode
    FROM Replays R
    INNER JOIN GameSessions G ON G.SessionId = R.SessionId
    WHERE R.SessionId = @SessionId
      AND (@PlayerId IS NULL OR R.PlayerId = @PlayerId);
END
GO

-- ============================================================
-- SECTION 4: SEED DATA
-- ============================================================

-- Restricted States (India)
INSERT INTO RestrictedStates (StateCode, StateName, Reason) VALUES
('TG', 'Telangana',       'State law prohibits real-money gaming'),
('AP', 'Andhra Pradesh',  'State law prohibits real-money gaming'),
('TN', 'Tamil Nadu',      'State law prohibits real-money gaming'),
('KL', 'Kerala',          'State law prohibits real-money gaming'),
('SK', 'Sikkim',          'Requires separate state gaming license'),
('MG', 'Meghalaya',       'Requires separate state gaming license'),
('NL', 'Nagaland',        'Requires separate state gaming license');
GO

-- Sample Levels (5 per time mode — populate all 500 in production)
INSERT INTO Levels (TimeMode, DifficultyScore, ArrowCount, GridCols, GridRows, Seed, GridJson) VALUES
('1min', 20,  8,  8,  8,  1001, '{"arrows":[{"id":1,"pts":[[0,0],[1,0],[2,0]],"dir":"R"},{"id":2,"pts":[[4,0],[4,1],[4,2]],"dir":"D"}]}'),
('1min', 35, 12, 10, 10,  1002, '{"arrows":[{"id":1,"pts":[[0,0],[1,0],[2,0],[3,0]],"dir":"R"}]}'),
('1min', 55, 16, 12, 12,  1003, '{"arrows":[{"id":1,"pts":[[0,0],[1,0],[2,0]],"dir":"R"}]}'),
('1min', 70, 18, 12, 14,  1004, '{"arrows":[{"id":1,"pts":[[0,0],[1,0],[2,0]],"dir":"R"}]}'),
('1min', 85, 20, 14, 14,  1005, '{"arrows":[{"id":1,"pts":[[0,0],[1,0],[2,0]],"dir":"R"}]}'),
('2min', 20,  8,  8,  8,  2001, '{"arrows":[{"id":1,"pts":[[0,0],[1,0],[2,0]],"dir":"R"}]}'),
('2min', 40, 14, 12, 12,  2002, '{"arrows":[{"id":1,"pts":[[0,0],[1,0],[2,0]],"dir":"R"}]}'),
('2min', 60, 18, 14, 14,  2003, '{"arrows":[{"id":1,"pts":[[0,0],[1,0],[2,0]],"dir":"R"}]}'),
('2min', 75, 20, 14, 14,  2004, '{"arrows":[{"id":1,"pts":[[0,0],[1,0],[2,0]],"dir":"R"}]}'),
('2min', 90, 22, 14, 16,  2005, '{"arrows":[{"id":1,"pts":[[0,0],[1,0],[2,0]],"dir":"R"}]}'),
('3min', 30, 12, 10, 10,  3001, '{"arrows":[{"id":1,"pts":[[0,0],[1,0],[2,0]],"dir":"R"}]}'),
('4min', 40, 14, 12, 12,  4001, '{"arrows":[{"id":1,"pts":[[0,0],[1,0],[2,0]],"dir":"R"}]}'),
('5min', 50, 16, 12, 14,  5001, '{"arrows":[{"id":1,"pts":[[0,0],[1,0],[2,0]],"dir":"R"}]}');
GO

PRINT 'SpeedSaga database created successfully!';
PRINT 'Tables: 12 | Stored Procedures: 15 | Indexes: 10';
GO