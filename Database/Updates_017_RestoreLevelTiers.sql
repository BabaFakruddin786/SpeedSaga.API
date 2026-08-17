-- SpeedSaga Updates 017: Restore Easy/Medium level slots for allocation + relax tier labels.
-- Runtime puzzles are generated procedurally; DB rows are ID slots for session tracking.
USE SpeedSagaDB;
GO

-- Re-label active levels so allocation can match new-player Easy curve.
UPDATE Levels SET PuzzleTier = CASE
    WHEN DifficultyScore >= 92 THEN 'SuperHard'
    WHEN DifficultyScore >= 84 THEN 'Hard'
    WHEN DifficultyScore >= 74 THEN 'Medium'
    ELSE 'Easy'
END
WHERE IsActive = 1;
GO

-- Ensure each time mode has at least one Easy slot (used only for LevelId tracking).
DECLARE @tm NVARCHAR(10), @tmCursor CURSOR;
SET @tmCursor = CURSOR FOR SELECT DISTINCT TimeMode FROM Levels;
OPEN @tmCursor;
FETCH NEXT FROM @tmCursor INTO @tm;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM Levels
        WHERE TimeMode = @tm AND IsActive = 1 AND PuzzleTier = 'Easy' AND ArrowCount >= 14
    )
    BEGIN
        UPDATE TOP (1) Levels
        SET IsActive = 1,
            PuzzleTier = 'Easy',
            DifficultyScore = CASE WHEN DifficultyScore > 78 THEN 72 ELSE DifficultyScore END,
            ArrowCount = CASE WHEN ArrowCount < 14 THEN 14 ELSE ArrowCount END
        WHERE TimeMode = @tm
          AND LevelId = (
              SELECT MIN(LevelId) FROM Levels l2 WHERE l2.TimeMode = @tm
          );
    END
    FETCH NEXT FROM @tmCursor INTO @tm;
END
CLOSE @tmCursor;
DEALLOCATE @tmCursor;
GO

PRINT 'Updates_017_RestoreLevelTiers applied.';
GO
