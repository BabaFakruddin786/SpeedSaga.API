-- SpeedSaga Updates 006: Varied puzzle allocation, difficulty scaling, move recording
USE SpeedSagaDB;
GO

-- ── Track which levels each player has played (avoid repeats) ──
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'PlayerLevelHistory')
CREATE TABLE PlayerLevelHistory (
    HistoryId       BIGINT IDENTITY(1,1) PRIMARY KEY,
    PlayerId        UNIQUEIDENTIFIER NOT NULL REFERENCES Players(PlayerId),
    LevelId         INT NOT NULL REFERENCES Levels(LevelId),
    SessionId       UNIQUEIDENTIFIER NOT NULL,
    EntryFeePaise   BIGINT NOT NULL DEFAULT 0,
    RewardMode      NVARCHAR(10) NOT NULL DEFAULT '3x',
    PlayedAt        DATETIME NOT NULL DEFAULT GETDATE(),
    INDEX IX_PlayerLevelHistory_Player (PlayerId, PlayedAt DESC)
);
GO

-- ── Per-move recording for replay / audit ──
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SessionMoves')
CREATE TABLE SessionMoves (
    MoveId          BIGINT IDENTITY(1,1) PRIMARY KEY,
    SessionId       UNIQUEIDENTIFIER NOT NULL,
    PlayerId        UNIQUEIDENTIFIER NOT NULL REFERENCES Players(PlayerId),
    MoveIndex       INT NOT NULL,
    Direction       NVARCHAR(4) NOT NULL,
    Col             INT NOT NULL,
    Row             INT NOT NULL,
    Timestamp       FLOAT NOT NULL,
    CreatedAt       DATETIME NOT NULL DEFAULT GETDATE(),
    CONSTRAINT UQ_SessionMove UNIQUE (SessionId, PlayerId, MoveIndex),
    INDEX IX_SessionMoves_Session (SessionId, MoveIndex)
);
GO

-- ── Improved level allocation ──
CREATE OR ALTER PROCEDURE USP_AllocateLevel
    @PlayerId       UNIQUEIDENTIFIER,
    @TimeMode       NVARCHAR(10),
    @RewardMode     NVARCHAR(10),   -- '3x' or '5x'
    @EntryFeePaise  BIGINT = 0,
    @LevelId        INT     OUTPUT,
    @GridJson       NVARCHAR(MAX) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @LevelId = NULL; SET @GridJson = NULL;

    DECLARE @WinRate DECIMAL(5,2) = 0;
    SELECT @WinRate = ISNULL(WinRatePct, 0) FROM PlayerStats WHERE PlayerId = @PlayerId;

    DECLARE @TargetWin DECIMAL(5,2) = CASE @RewardMode WHEN '3x' THEN 20.00 WHEN '5x' THEN 10.00 ELSE 20.00 END;

    -- Games played in this reward mode (single-player paid)
    DECLARE @GamesInMode INT = 0;
    SELECT @GamesInMode = COUNT(*)
    FROM GameSessions
    WHERE Player1Id = @PlayerId
      AND GameMode LIKE 'SinglePlayer%'
      AND RewardMode = @RewardMode
      AND Status IN ('Active', 'Complete');

    -- Base difficulty from game-count curve (1-2 medium, rest tough for 3x)
    DECLARE @MinDiff INT, @MaxDiff INT;

    IF @RewardMode = '5x'
    BEGIN
        IF @GamesInMode < 1
            SELECT @MinDiff = 40, @MaxDiff = 60;   -- first game only: medium
        ELSE
            SELECT @MinDiff = 75, @MaxDiff = 100;  -- rest: very tough
    END
    ELSE IF @EntryFeePaise = 0
    BEGIN
        -- Free practice: easy-medium variety
        SELECT @MinDiff = 15, @MaxDiff = 50;
    END
    ELSE
    BEGIN
        IF @GamesInMode < 2
            SELECT @MinDiff = 30, @MaxDiff = 55;   -- first 2 games: medium
        ELSE
            SELECT @MinDiff = 65, @MaxDiff = 95;   -- rest: tough
    END

    -- Higher entry fee = harder floor (protects platform on big rewards)
    DECLARE @FeeBoost INT = CASE
        WHEN @EntryFeePaise >= 200000 THEN 20   -- Rs 2000+
        WHEN @EntryFeePaise >= 100000 THEN 15   -- Rs 1000+
        WHEN @EntryFeePaise >= 50000  THEN 12   -- Rs 500+
        WHEN @EntryFeePaise >= 30000  THEN 10   -- Rs 300+
        WHEN @EntryFeePaise >= 20000  THEN 8    -- Rs 200+
        WHEN @EntryFeePaise >= 10000  THEN 5    -- Rs 100+
        ELSE 0
    END;
    SET @MinDiff = @MinDiff + @FeeBoost;
    SET @MaxDiff = @MaxDiff + @FeeBoost;
    IF @MaxDiff > 100 SET @MaxDiff = 100;
    IF @MinDiff > @MaxDiff SET @MinDiff = @MaxDiff - 5;
    IF @MinDiff < 10 SET @MinDiff = 10;

    -- Win-rate fine-tuning (only for paid modes)
    IF @EntryFeePaise > 0
    BEGIN
        IF @WinRate > @TargetWin + 10 AND @MinDiff < 80
            SET @MinDiff = 80;
        IF @WinRate > @TargetWin + 5 AND @MaxDiff < 90
            SET @MaxDiff = CASE WHEN @MaxDiff + 10 > 100 THEN 100 ELSE @MaxDiff + 10 END;
        IF @WinRate < @TargetWin - 15 AND @GamesInMode < 2
        BEGIN
            -- Retention only on first 2 games, never drop below medium
            IF @MinDiff > 25 SET @MinDiff = 25;
        END
    END

    -- Exclude recently played levels (last 50)
    SELECT TOP 1 @LevelId = LevelId, @GridJson = GridJson
    FROM Levels
    WHERE TimeMode = @TimeMode
      AND DifficultyScore BETWEEN @MinDiff AND @MaxDiff
      AND IsActive = 1
      AND LevelId NOT IN (
          SELECT TOP 50 LevelId FROM PlayerLevelHistory
          WHERE PlayerId = @PlayerId ORDER BY PlayedAt DESC
      )
    ORDER BY NEWID();

    -- Relax exclusion to last 10 if pool empty
    IF @LevelId IS NULL
        SELECT TOP 1 @LevelId = LevelId, @GridJson = GridJson
        FROM Levels
        WHERE TimeMode = @TimeMode
          AND DifficultyScore BETWEEN @MinDiff AND @MaxDiff
          AND IsActive = 1
          AND LevelId NOT IN (
              SELECT TOP 10 LevelId FROM PlayerLevelHistory
              WHERE PlayerId = @PlayerId ORDER BY PlayedAt DESC
          )
        ORDER BY NEWID();

    -- Fallback: any active level for time mode, still avoid last 3 played
    IF @LevelId IS NULL
        SELECT TOP 1 @LevelId = LevelId, @GridJson = GridJson
        FROM Levels
        WHERE TimeMode = @TimeMode AND IsActive = 1
          AND LevelId NOT IN (
              SELECT TOP 3 LevelId FROM PlayerLevelHistory
              WHERE PlayerId = @PlayerId ORDER BY PlayedAt DESC
          )
        ORDER BY NEWID();

    -- Last resort: any level
    IF @LevelId IS NULL
        SELECT TOP 1 @LevelId = LevelId, @GridJson = GridJson
        FROM Levels WHERE TimeMode = @TimeMode AND IsActive = 1
        ORDER BY NEWID();
END
GO

-- ── Record level when session starts ──
CREATE OR ALTER PROCEDURE USP_RecordLevelPlayed
    @PlayerId       UNIQUEIDENTIFIER,
    @LevelId        INT,
    @SessionId      UNIQUEIDENTIFIER,
    @EntryFeePaise  BIGINT = 0,
    @RewardMode     NVARCHAR(10) = '3x'
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO PlayerLevelHistory (PlayerId, LevelId, SessionId, EntryFeePaise, RewardMode)
    VALUES (@PlayerId, @LevelId, @SessionId, @EntryFeePaise, @RewardMode);
END
GO

-- ── Record each move during gameplay ──
CREATE OR ALTER PROCEDURE USP_RecordSessionMove
    @SessionId      UNIQUEIDENTIFIER,
    @PlayerId       UNIQUEIDENTIFIER,
    @Direction      NVARCHAR(4),
    @Col            INT,
    @Row            INT,
    @Timestamp      FLOAT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @NextIndex INT = 1;
    SELECT @NextIndex = ISNULL(MAX(MoveIndex), 0) + 1
    FROM SessionMoves WHERE SessionId = @SessionId AND PlayerId = @PlayerId;

    INSERT INTO SessionMoves (SessionId, PlayerId, MoveIndex, Direction, Col, Row, Timestamp)
    VALUES (@SessionId, @PlayerId, @NextIndex, @Direction, @Col, @Row, @Timestamp);
END
GO

-- ── Player game history for profile / preview ──
CREATE OR ALTER PROCEDURE USP_GetPlayerGameHistory
    @PlayerId   UNIQUEIDENTIFIER,
    @Page       INT = 1,
    @PageSize   INT = 20
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Offset INT = (@Page - 1) * @PageSize;

    SELECT
        GS.SessionId,
        GS.GameMode,
        GS.RewardMode,
        GS.EntryFeePaise,
        GS.RewardPaise,
        GS.LevelId,
        GS.Status,
        GS.StartedAt,
        GS.CompletedAt,
        CASE WHEN GS.WinnerId = @PlayerId THEN 1 ELSE 0 END AS IsWon,
        ISNULL(R.TotalMoves, (SELECT COUNT(*) FROM SessionMoves SM WHERE SM.SessionId = GS.SessionId AND SM.PlayerId = @PlayerId)) AS TotalMoves,
        R.SolvedInSecs,
        GS.IsReplayAvailable,
        (SELECT COUNT(*) FROM SessionMoves SM WHERE SM.SessionId = GS.SessionId AND SM.PlayerId = @PlayerId) AS RecordedMoves
    FROM GameSessions GS
    LEFT JOIN Replays R ON R.SessionId = GS.SessionId AND R.PlayerId = @PlayerId
    WHERE GS.Player1Id = @PlayerId OR GS.Player2Id = @PlayerId
    ORDER BY GS.StartedAt DESC
    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
END
GO

-- ── Replay: prefer live-recorded moves, fall back to end-of-game JSON ──
CREATE OR ALTER PROCEDURE USP_GetReplay
    @SessionId  UNIQUEIDENTIFIER,
    @PlayerId   UNIQUEIDENTIFIER    = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @MovesJson NVARCHAR(MAX) = NULL;
    DECLARE @TotalMoves INT = 0;
    DECLARE @SolvedSecs INT = NULL;
    DECLARE @ReplayPlayer UNIQUEIDENTIFIER = @PlayerId;

    -- Assemble from SessionMoves if available
    IF @PlayerId IS NOT NULL AND EXISTS (SELECT 1 FROM SessionMoves WHERE SessionId = @SessionId AND PlayerId = @PlayerId)
    BEGIN
        SELECT @MovesJson = (
            SELECT Direction AS dir, Col AS col, Row AS row, Timestamp AS timestamp
            FROM SessionMoves
            WHERE SessionId = @SessionId AND PlayerId = @PlayerId
            ORDER BY MoveIndex
            FOR JSON PATH
        );
        SELECT @TotalMoves = COUNT(*) FROM SessionMoves WHERE SessionId = @SessionId AND PlayerId = @PlayerId;
    END

    -- Fall back to Replays table
    IF @MovesJson IS NULL
    BEGIN
        SELECT TOP 1
            @MovesJson = R.MovesJson,
            @TotalMoves = R.TotalMoves,
            @SolvedSecs = R.SolvedInSecs,
            @ReplayPlayer = R.PlayerId
        FROM Replays R
        WHERE R.SessionId = @SessionId
          AND (@PlayerId IS NULL OR R.PlayerId = @PlayerId)
        ORDER BY R.CreatedAt DESC;
    END
    ELSE
    BEGIN
        SELECT TOP 1 @SolvedSecs = R.SolvedInSecs
        FROM Replays R
        WHERE R.SessionId = @SessionId AND R.PlayerId = @PlayerId;
    END

    SELECT
        @SessionId AS SessionId,
        @ReplayPlayer AS PlayerId,
        @MovesJson AS MovesJson,
        @TotalMoves AS TotalMoves,
        @SolvedSecs AS SolvedInSecs,
        G.LevelId,
        G.TimeLimitSecs,
        G.GameMode,
        G.EntryFeePaise,
        G.RewardPaise,
        G.StartedAt,
        G.CompletedAt
    FROM GameSessions G
    WHERE G.SessionId = @SessionId;
END
GO

PRINT 'Updates_006_LevelAllocationAndMoves applied.';
GO
