-- SpeedSaga Updates 008: Hard difficulty for all modes + filter simple puzzles
USE SpeedSagaDB;
GO

-- Deactivate trivial puzzles (short arrows / tiny JSON)
UPDATE Levels SET IsActive = 0
WHERE ArrowCount < 7 OR LEN(ISNULL(GridJson, '')) < 220;
GO

-- Align difficulty score with real complexity
UPDATE Levels SET DifficultyScore =
    CASE
        WHEN ArrowCount >= 12 THEN 88 + (LevelId % 13)
        WHEN ArrowCount >= 10 THEN 80 + (LevelId % 15)
        WHEN ArrowCount >= 8  THEN 74 + (LevelId % 18)
        ELSE 68 + (LevelId % 22)
    END
WHERE IsActive = 1;
GO

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

    -- Always hard — no beginner/easy bracket
    DECLARE @MinDiff INT, @MaxDiff INT, @MinArrows INT;

    IF @RewardMode = '5x'
    BEGIN
        SET @MinDiff = 85; SET @MaxDiff = 100; SET @MinArrows = 10;
    END
    ELSE IF @EntryFeePaise = 0
    BEGIN
        SET @MinDiff = 68; SET @MaxDiff = 92; SET @MinArrows = 7;
    END
    ELSE
    BEGIN
        SET @MinDiff = 75; SET @MaxDiff = 100; SET @MinArrows = 8;
        IF @EntryFeePaise >= 50000  SET @MinArrows = 10;
        IF @EntryFeePaise >= 100000 SET @MinArrows = 11;
        IF @EntryFeePaise >= 200000 BEGIN SET @MinDiff = 82; SET @MinArrows = 12; END
    END

    -- High win-rate players get even harder puzzles (never easier)
    IF @WinRate > @TargetWin + 5 AND @MinDiff < 88 SET @MinDiff = 88;
    IF @WinRate > @TargetWin + 10 AND @MinDiff < 92 SET @MinDiff = 92;

    DECLARE @ExcludeCount INT = 50;

    WHILE @ExcludeCount >= 3 AND @LevelId IS NULL
    BEGIN
        SELECT TOP 1 @LevelId = l.LevelId, @GridJson = l.GridJson
        FROM Levels l WITH (NOLOCK)
        WHERE l.TimeMode = @TimeMode
          AND l.DifficultyScore BETWEEN @MinDiff AND @MaxDiff
          AND l.ArrowCount >= @MinArrows
          AND l.IsActive = 1
          AND NOT EXISTS (
              SELECT 1 FROM (
                  SELECT TOP (@ExcludeCount) h.LevelId AS Lid
                  FROM PlayerLevelHistory h WITH (NOLOCK)
                  WHERE h.PlayerId = @PlayerId
                  ORDER BY h.PlayedAt DESC
              ) recent WHERE recent.Lid = l.LevelId
          )
        ORDER BY l.ArrowCount DESC, l.DifficultyScore DESC, NEWID();

        SET @ExcludeCount = CASE @ExcludeCount WHEN 50 THEN 10 WHEN 10 THEN 3 ELSE 0 END;
    END

    IF @LevelId IS NULL
        SELECT TOP 1 @LevelId = l.LevelId, @GridJson = l.GridJson
        FROM Levels l WITH (NOLOCK)
        WHERE l.TimeMode = @TimeMode AND l.IsActive = 1 AND l.ArrowCount >= @MinArrows
          AND l.DifficultyScore >= @MinDiff
        ORDER BY l.ArrowCount DESC, NEWID();

    IF @LevelId IS NULL
        SELECT TOP 1 @LevelId = l.LevelId, @GridJson = l.GridJson
        FROM Levels l WITH (NOLOCK)
        WHERE l.TimeMode = @TimeMode AND l.IsActive = 1
        ORDER BY l.DifficultyScore DESC, l.ArrowCount DESC, NEWID();
END
GO

PRINT 'Updates_008_HardDifficulty applied.';
GO
