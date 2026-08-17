-- SpeedSaga Updates 007: Performance indexes for 10k+ concurrent players
USE SpeedSagaDB;
GO

-- Fast level allocation by time mode + difficulty band
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Levels_Allocate' AND object_id = OBJECT_ID('Levels'))
    CREATE NONCLUSTERED INDEX IX_Levels_Allocate
    ON Levels (TimeMode, IsActive, DifficultyScore)
    INCLUDE (LevelId, GridJson);
GO

-- Fast anti-repeat lookup for played levels
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PlayerLevelHistory_PlayerLevel' AND object_id = OBJECT_ID('PlayerLevelHistory'))
    CREATE NONCLUSTERED INDEX IX_PlayerLevelHistory_PlayerLevel
    ON PlayerLevelHistory (PlayerId, LevelId, PlayedAt DESC);
GO

-- Fast move polling / replay assembly
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SessionMoves_SessionPlayer' AND object_id = OBJECT_ID('SessionMoves'))
    CREATE NONCLUSTERED INDEX IX_SessionMoves_SessionPlayer
    ON SessionMoves (SessionId, PlayerId, MoveIndex);
GO

-- Game-count difficulty curve
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_GameSessions_PlayerMode' AND object_id = OBJECT_ID('GameSessions'))
    CREATE NONCLUSTERED INDEX IX_GameSessions_PlayerMode
    ON GameSessions (Player1Id, RewardMode, Status)
    INCLUDE (SessionId, StartedAt);
GO

-- Batch move insert (used by background persistence worker)
CREATE OR ALTER PROCEDURE USP_RecordSessionMovesBatch
    @MovesJson NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    IF @MovesJson IS NULL OR LEN(@MovesJson) < 2 RETURN;

    INSERT INTO SessionMoves (SessionId, PlayerId, MoveIndex, Direction, Col, Row, Timestamp)
    SELECT
        TRY_CAST(JSON_VALUE(m.value, '$.sessionId') AS UNIQUEIDENTIFIER),
        TRY_CAST(JSON_VALUE(m.value, '$.playerId') AS UNIQUEIDENTIFIER),
        TRY_CAST(JSON_VALUE(m.value, '$.moveIndex') AS INT),
        JSON_VALUE(m.value, '$.direction'),
        TRY_CAST(JSON_VALUE(m.value, '$.col') AS INT),
        TRY_CAST(JSON_VALUE(m.value, '$.row') AS INT),
        TRY_CAST(JSON_VALUE(m.value, '$.timestamp') AS FLOAT)
    FROM OPENJSON(@MovesJson) m
    WHERE TRY_CAST(JSON_VALUE(m.value, '$.sessionId') AS UNIQUEIDENTIFIER) IS NOT NULL
      AND NOT EXISTS (
          SELECT 1 FROM SessionMoves sm
          WHERE sm.SessionId = TRY_CAST(JSON_VALUE(m.value, '$.sessionId') AS UNIQUEIDENTIFIER)
            AND sm.PlayerId = TRY_CAST(JSON_VALUE(m.value, '$.playerId') AS UNIQUEIDENTIFIER)
            AND sm.MoveIndex = TRY_CAST(JSON_VALUE(m.value, '$.moveIndex') AS INT)
      );
END
GO

-- Optimized allocation: NOT EXISTS instead of NOT IN, indexed lookups
CREATE OR ALTER PROCEDURE USP_AllocateLevel
    @PlayerId       UNIQUEIDENTIFIER,
    @TimeMode       NVARCHAR(10),
    @RewardMode     NVARCHAR(10),
    @EntryFeePaise  BIGINT = 0,
    @LevelId        INT     OUTPUT,
    @GridJson       NVARCHAR(MAX) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @LevelId = NULL; SET @GridJson = NULL;

    DECLARE @WinRate DECIMAL(5,2) = 0;
    SELECT @WinRate = ISNULL(WinRatePct, 0) FROM PlayerStats WITH (NOLOCK) WHERE PlayerId = @PlayerId;

    DECLARE @TargetWin DECIMAL(5,2) = CASE @RewardMode WHEN '3x' THEN 20.00 WHEN '5x' THEN 10.00 ELSE 20.00 END;

    DECLARE @GamesInMode INT = 0;
    SELECT @GamesInMode = COUNT(*)
    FROM GameSessions WITH (NOLOCK)
    WHERE Player1Id = @PlayerId AND GameMode LIKE 'SinglePlayer%'
      AND RewardMode = @RewardMode AND Status IN ('Active', 'Complete');

    DECLARE @MinDiff INT, @MaxDiff INT;
    IF @RewardMode = '5x'
    BEGIN
        IF @GamesInMode < 1 SELECT @MinDiff = 40, @MaxDiff = 60;
        ELSE SELECT @MinDiff = 75, @MaxDiff = 100;
    END
    ELSE IF @EntryFeePaise = 0
        SELECT @MinDiff = 15, @MaxDiff = 50;
    ELSE
    BEGIN
        IF @GamesInMode < 2 SELECT @MinDiff = 30, @MaxDiff = 55;
        ELSE SELECT @MinDiff = 65, @MaxDiff = 95;
    END

    DECLARE @FeeBoost INT = CASE
        WHEN @EntryFeePaise >= 200000 THEN 20 WHEN @EntryFeePaise >= 100000 THEN 15
        WHEN @EntryFeePaise >= 50000 THEN 12 WHEN @EntryFeePaise >= 30000 THEN 10
        WHEN @EntryFeePaise >= 20000 THEN 8 WHEN @EntryFeePaise >= 10000 THEN 5 ELSE 0 END;
    SET @MinDiff = @MinDiff + @FeeBoost; SET @MaxDiff = @MaxDiff + @FeeBoost;
    IF @MaxDiff > 100 SET @MaxDiff = 100;
    IF @MinDiff > @MaxDiff SET @MinDiff = @MaxDiff - 5;
    IF @MinDiff < 10 SET @MinDiff = 10;

    IF @EntryFeePaise > 0
    BEGIN
        IF @WinRate > @TargetWin + 10 AND @MinDiff < 80 SET @MinDiff = 80;
        IF @WinRate > @TargetWin + 5 AND @MaxDiff < 90 SET @MaxDiff = CASE WHEN @MaxDiff + 10 > 100 THEN 100 ELSE @MaxDiff + 10 END;
    END

    DECLARE @ExcludeCount INT = 50;

    WHILE @ExcludeCount >= 3 AND @LevelId IS NULL
    BEGIN
        SELECT TOP 1 @LevelId = l.LevelId, @GridJson = l.GridJson
        FROM Levels l WITH (NOLOCK)
        WHERE l.TimeMode = @TimeMode
          AND l.DifficultyScore BETWEEN @MinDiff AND @MaxDiff
          AND l.IsActive = 1
          AND NOT EXISTS (
              SELECT 1 FROM (
                  SELECT TOP (@ExcludeCount) h.LevelId AS Lid
                  FROM PlayerLevelHistory h WITH (NOLOCK)
                  WHERE h.PlayerId = @PlayerId
                  ORDER BY h.PlayedAt DESC
              ) recent WHERE recent.Lid = l.LevelId
          )
        ORDER BY NEWID();

        SET @ExcludeCount = CASE @ExcludeCount WHEN 50 THEN 10 WHEN 10 THEN 3 ELSE 0 END;
    END

    IF @LevelId IS NULL
        SELECT TOP 1 @LevelId = l.LevelId, @GridJson = l.GridJson
        FROM Levels l WITH (NOLOCK)
        WHERE l.TimeMode = @TimeMode AND l.IsActive = 1
        ORDER BY NEWID();
END
GO

PRINT 'Updates_007_ScaleIndexes applied.';
GO
