USE SpeedSagaDB;
GO
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE dbo.USP_AdminGetLevelSummary
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        COUNT(*) AS TotalLevels,
        SUM(CASE WHEN IsActive = 1 THEN 1 ELSE 0 END) AS ActiveLevels,
        SUM(CASE WHEN IsActive = 0 THEN 1 ELSE 0 END) AS InactiveLevels
    FROM dbo.Levels;

    SELECT TimeMode, COUNT(*) AS LevelCount,
           SUM(CASE WHEN IsActive = 1 THEN 1 ELSE 0 END) AS ActiveCount
    FROM dbo.Levels
    GROUP BY TimeMode
    ORDER BY TimeMode;

    SELECT PuzzleTier, COUNT(*) AS LevelCount,
           SUM(CASE WHEN IsActive = 1 THEN 1 ELSE 0 END) AS ActiveCount
    FROM dbo.Levels
    GROUP BY PuzzleTier
    ORDER BY PuzzleTier;
END
GO

CREATE OR ALTER PROCEDURE dbo.USP_AdminListLevels
    @TimeMode       NVARCHAR(10) = NULL,
    @PuzzleTier     NVARCHAR(12) = NULL,
    @IsActive       BIT = NULL,
    @Page           INT = 1,
    @PageSize       INT = 50
AS
BEGIN
    SET NOCOUNT ON;
    IF @Page < 1 SET @Page = 1;
    IF @PageSize < 1 OR @PageSize > 200 SET @PageSize = 50;

    DECLARE @Offset INT = (@Page - 1) * @PageSize;

    SELECT COUNT(*) AS TotalCount
    FROM dbo.Levels l
    WHERE (@TimeMode IS NULL OR l.TimeMode = @TimeMode)
      AND (@PuzzleTier IS NULL OR l.PuzzleTier = @PuzzleTier)
      AND (@IsActive IS NULL OR l.IsActive = @IsActive);

    SELECT l.LevelId, l.TimeMode, l.PuzzleTier, l.DifficultyScore, l.ArrowCount,
           l.GridCols, l.GridRows, l.Seed, l.IsActive, l.CreatedAt
    FROM dbo.Levels l
    WHERE (@TimeMode IS NULL OR l.TimeMode = @TimeMode)
      AND (@PuzzleTier IS NULL OR l.PuzzleTier = @PuzzleTier)
      AND (@IsActive IS NULL OR l.IsActive = @IsActive)
    ORDER BY l.LevelId
    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
END
GO

CREATE OR ALTER PROCEDURE dbo.USP_AdminExpandLevelPool
    @TargetTotal    INT,
    @Result         INT OUTPUT,
    @Message        NVARCHAR(200) OUTPUT,
    @Added          INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @Result = 0;
    SET @Added = 0;
    SET @Message = N'';

    IF @TargetTotal IS NULL OR @TargetTotal < 1
    BEGIN
        SET @Result = -1;
        SET @Message = N'Target total must be at least 1.';
        RETURN;
    END

    IF @TargetTotal > 20000
    BEGIN
        SET @Result = -2;
        SET @Message = N'Target total cannot exceed 20,000.';
        RETURN;
    END

    DECLARE @Current INT = (SELECT COUNT(*) FROM dbo.Levels);
    IF @TargetTotal <= @Current
    BEGIN
        SET @Result = -3;
        SET @Message = N'Current pool already has ' + CAST(@Current AS NVARCHAR(10)) + N' levels. Choose a higher target.';
        RETURN;
    END

    DECLARE @cnt INT = @Current;
    DECLARE @placeholder NVARCHAR(400) = N'{"cols":32,"rows":32,"runtime":true,"arrows":[{"id":1,"pts":[[0,0],[1,0],[2,0],[3,0],[4,0]],"dir":"R"},{"id":2,"pts":[[6,0],[6,1],[6,2],[6,3]],"dir":"D"}]}';

    WHILE @cnt < @TargetTotal
    BEGIN
        DECLARE @lid INT = @cnt + 1;
        DECLARE @tierIdx INT = @lid % 4;
        DECLARE @tier NVARCHAR(12) = CASE @tierIdx
            WHEN 0 THEN N'Easy'
            WHEN 1 THEN N'Medium'
            WHEN 2 THEN N'Hard'
            ELSE N'SuperHard' END;
        DECLARE @arrowCount INT = CASE @tier
            WHEN N'Easy' THEN 30
            WHEN N'Medium' THEN 50
            WHEN N'Hard' THEN 80
            ELSE 120 END;
        DECLARE @diff INT = CASE @tier
            WHEN N'Easy' THEN 65 + (@lid % 14)
            WHEN N'Medium' THEN 76 + (@lid % 13)
            WHEN N'Hard' THEN 86 + (@lid % 10)
            ELSE 92 + (@lid % 9) END;
        DECLARE @tm NVARCHAR(10) = CASE (@lid % 5)
            WHEN 0 THEN N'1min'
            WHEN 1 THEN N'2min'
            WHEN 2 THEN N'3min'
            WHEN 3 THEN N'4min'
            ELSE N'5min' END;

        INSERT INTO dbo.Levels (TimeMode, DifficultyScore, ArrowCount, GridCols, GridRows, Seed, GridJson, IsActive, PuzzleTier)
        VALUES (@tm, @diff, @arrowCount, 32, 32, 20000 + @lid, @placeholder, 1, @tier);

        SET @cnt = @cnt + 1;
        SET @Added = @Added + 1;
    END

    SET @Message = N'Added ' + CAST(@Added AS NVARCHAR(10)) + N' level slot(s). Pool size is now ' + CAST(@TargetTotal AS NVARCHAR(10)) + N'.';
END
GO

CREATE OR ALTER PROCEDURE dbo.USP_AdminSetLevelActive
    @LevelId        INT,
    @IsActive       BIT,
    @Result         INT OUTPUT,
    @Message        NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @Result = 0;

    IF NOT EXISTS (SELECT 1 FROM dbo.Levels WHERE LevelId = @LevelId)
    BEGIN
        SET @Result = -1;
        SET @Message = N'Level not found.';
        RETURN;
    END

    UPDATE dbo.Levels SET IsActive = @IsActive WHERE LevelId = @LevelId;
    SET @Message = CASE WHEN @IsActive = 1 THEN N'Level activated.' ELSE N'Level deactivated.' END;
END
GO

PRINT 'Updates_037_AdminGameLevels applied.';
GO
