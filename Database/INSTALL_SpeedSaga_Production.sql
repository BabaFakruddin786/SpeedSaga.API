-- ============================================================
-- SPEEDSAGA — PRODUCTION DATABASE INSTALL (SSMS)
-- ============================================================
--
-- Creates SpeedSagaDB with full schema, stored procedures,
-- puzzle levels, gameplay config, support/ticker defaults,
-- and admin login accounts.
--
-- HOW TO RUN (SSMS connected to your Ubuntu SQL Server):
--   1. Keep this entire Database folder on your PC (same directory
--      as SpeedSagaDB.sql and all Updates_*.sql files).
--   2. In SSMS: Query menu → SQLCMD Mode (must be ON).
--   3. Open this file and Execute (F5).
--
-- Fresh install only — do not run on a database that already has
-- player data unless you intend to recreate everything.
--
-- Default admin logins (change passwords after first login):
--   admin@speedsaga.com   / Admin@123456   (SuperAdmin)
--   support@speedsaga.com / Support@123456 (Support)
--
-- Other seed data included via migrations:
--   Restricted states, puzzle levels, reward/time/entry fees,
--   app support (support@speedsaga.com, 9052916052),
--   ticker messages, themes, gameplay lives, etc.
-- ============================================================

:setvar DatabaseName "SpeedSagaDB"

USE master;
GO

IF OBJECT_ID(N'SpeedSagaDB.dbo.Players', N'U') IS NOT NULL
BEGIN
    RAISERROR(N'SpeedSagaDB already exists with tables. Drop the database first for a clean install, or run RunAllMigrations.ps1 -UpdatesOnly for an existing DB.', 16, 1);
    SET NOEXEC ON;
END
GO

IF DB_ID(N'$(DatabaseName)') IS NULL
BEGIN
    CREATE DATABASE [$(DatabaseName)];
    PRINT 'Created database $(DatabaseName).';
END
GO

:r SpeedSagaDB.sql

:r Updates_002_UnityFeatures.sql
:r Updates_003_FixUniqueNulls.sql
:r Updates_004_PlayablePuzzles.sql
:r Updates_005_AllFeatures.sql
:r Updates_006_LevelAllocationAndMoves.sql
:r Updates_007_ScaleIndexes.sql
:r Updates_008_HardDifficulty.sql
:r Updates_009_ComplexPuzzleTiers.sql
:r Updates_010_ArrowCountTiers.sql
:r Updates_011_ArrowCountTiersV2.sql
:r Updates_012_KycVerification.sql
:r Updates_013_OtpMessaging.sql
:r Updates_014_LaunchFeatures.sql
:r Updates_015_GameHistoryTimeLimit.sql
:r Updates_016_RegisterValidation.sql
:r Updates_017_RestoreLevelTiers.sql
:r Updates_018_ExcludePracticeFromHistory.sql
:r Updates_019_WithdrawOnlyGeoblock.sql
:r Updates_020_AppThemes.sql
:r Updates_021_ThemePaletteRefresh.sql
:r Updates_022_LinkPlayerContact.sql
:r Updates_024_GamePlayConfig.sql
:r Updates_025_FreePlayTimeModes.sql
:r Updates_026_GamePlayLives.sql
:r Updates_027_KycDocumentReview.sql
:r Updates_028_SupportChat.sql
:r Updates_029_ProfileKycCompletion.sql
:r Updates_030_AppSupportConfig.sql
:r Updates_031_AppTickerConfig.sql
:r Updates_032_GameType.sql
:r Updates_033_AdminConsole.sql
:r Updates_034_AdminUsers.sql
:r Updates_035_AdminPhase2.sql
:r Updates_036_AdminPlayerBan.sql
:r Updates_037_AdminGameLevels.sql
:r Updates_038_AdminLevelSummaryDetail.sql
:r Updates_039_AdminPurgeInactiveLevels.sql

:r Updates_040_AdminTestDataPurge.sql

:r Production_SeedAdminUsers.sql

USE SpeedSagaDB;
GO

PRINT '';
PRINT '============================================================';
PRINT ' SpeedSaga production database install completed.';
PRINT ' Database: SpeedSagaDB';
PRINT ' Admin:    admin@speedsaga.com / Admin@123456';
PRINT ' Support:  support@speedsaga.com / Support@123456';
PRINT '============================================================';
GO

SET NOEXEC OFF;
GO
