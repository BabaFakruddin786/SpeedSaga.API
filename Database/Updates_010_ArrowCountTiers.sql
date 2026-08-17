-- SpeedSaga Updates 010: Arrow count tiers (Easy 80, Medium 150, Hard 200, SuperHard 250, min 50)
USE SpeedSagaDB;
GO

-- Puzzles are now generated procedurally on the API; DB stores session metadata only.
-- Update tier labels for analytics / future seeding.
UPDATE Levels SET PuzzleTier = CASE
    WHEN DifficultyScore >= 92 THEN 'SuperHard'
    WHEN DifficultyScore >= 84 THEN 'Hard'
    WHEN DifficultyScore >= 74 THEN 'Medium'
    ELSE 'Easy'
END WHERE IsActive = 1;
GO

PRINT 'Updates_010_ArrowCountTiers applied. Puzzles generated at runtime: Easy=80, Medium=150, Hard=200, SuperHard=250 (min 50).';
GO
