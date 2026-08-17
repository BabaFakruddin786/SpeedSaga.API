USE SpeedSagaDB;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AppThemes')
BEGIN
    CREATE TABLE AppThemes (
        ThemeId         INT             IDENTITY(1,1) PRIMARY KEY,
        ThemeCode       NVARCHAR(50)    NOT NULL,
        ThemeName       NVARCHAR(100)   NOT NULL,
        AppearanceMode  NVARCHAR(10)    NOT NULL,
        IsActive        BIT             NOT NULL DEFAULT 0,
        SortOrder       INT             NOT NULL DEFAULT 0,
        Bg              NVARCHAR(10)    NOT NULL,
        Surface         NVARCHAR(10)    NOT NULL,
        Card            NVARCHAR(10)    NOT NULL,
        Border          NVARCHAR(10)    NOT NULL,
        Gold            NVARCHAR(10)    NOT NULL,
        GoldDim         NVARCHAR(10)    NOT NULL,
        Accent          NVARCHAR(10)    NOT NULL,
        AccentBright    NVARCHAR(10)    NOT NULL,
        Green           NVARCHAR(10)    NOT NULL,
        Red             NVARCHAR(10)    NOT NULL,
        Orange          NVARCHAR(10)    NOT NULL,
        TextColor       NVARCHAR(10)    NOT NULL,
        TextMuted       NVARCHAR(10)    NOT NULL,
        TextDim         NVARCHAR(10)    NOT NULL,
        WalletTop       NVARCHAR(10)    NOT NULL,
        WalletBottom    NVARCHAR(10)    NOT NULL,
        FreeModeTop     NVARCHAR(10)    NOT NULL,
        FreeModeBottom  NVARCHAR(10)    NOT NULL,
        PremiumTop      NVARCHAR(10)    NOT NULL,
        PremiumBottom   NVARCHAR(10)    NOT NULL,
        UpdatedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_AppThemes_Code UNIQUE (ThemeCode),
        CONSTRAINT CK_AppThemes_AppearanceMode CHECK (AppearanceMode IN ('Dark', 'Light'))
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_AppThemes_SingleActive')
    CREATE UNIQUE INDEX UX_AppThemes_SingleActive ON AppThemes(IsActive) WHERE IsActive = 1;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Players') AND name = 'AppearanceMode')
BEGIN
    ALTER TABLE Players ADD AppearanceMode NVARCHAR(10) NOT NULL
        CONSTRAINT DF_Players_AppearanceMode DEFAULT 'Dark'
        CONSTRAINT CK_Players_AppearanceMode CHECK (AppearanceMode IN ('Dark', 'Light'));
END
GO

CREATE OR ALTER TRIGGER TR_AppThemes_EnforceSingleActive
ON AppThemes
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF (SELECT COUNT(*) FROM AppThemes WHERE IsActive = 1) > 1
    BEGIN
        RAISERROR('Only one AppThemes row may have IsActive = 1.', 16, 1);
        ROLLBACK TRANSACTION;
        RETURN;
    END
END
GO

CREATE OR ALTER PROCEDURE USP_GetAppTheme
    @AppearanceMode NVARCHAR(10)
AS
BEGIN
    SET NOCOUNT ON;
    IF @AppearanceMode NOT IN ('Dark', 'Light')
        SET @AppearanceMode = 'Dark';

    SELECT TOP 1
        ThemeCode, ThemeName, AppearanceMode, IsActive,
        Bg, Surface, Card, Border, Gold, GoldDim, Accent, AccentBright,
        Green, Red, Orange, TextColor, TextMuted, TextDim,
        WalletTop, WalletBottom, FreeModeTop, FreeModeBottom, PremiumTop, PremiumBottom,
        UpdatedAt
    FROM AppThemes
    WHERE AppearanceMode = @AppearanceMode
    ORDER BY IsActive DESC, SortOrder ASC, ThemeId ASC;
END
GO

CREATE OR ALTER PROCEDURE USP_GetAppThemeDefault
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP 1 AppearanceMode
    FROM AppThemes
    WHERE IsActive = 1
    ORDER BY ThemeId ASC;

    IF @@ROWCOUNT = 0
        SELECT 'Dark' AS AppearanceMode;
END
GO

CREATE OR ALTER PROCEDURE USP_GetAllAppThemes
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ThemeCode, ThemeName, AppearanceMode, IsActive, SortOrder, UpdatedAt
    FROM AppThemes
    ORDER BY SortOrder ASC, ThemeId ASC;
END
GO

CREATE OR ALTER PROCEDURE USP_SetPlayerAppearance
    @PlayerId       UNIQUEIDENTIFIER,
    @AppearanceMode NVARCHAR(10),
    @Result         INT OUTPUT,
    @Message        NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    IF @AppearanceMode NOT IN ('Dark', 'Light')
    BEGIN SET @Result = -1; SET @Message = 'AppearanceMode must be Dark or Light.'; RETURN; END

    IF NOT EXISTS (SELECT 1 FROM AppThemes WHERE AppearanceMode = @AppearanceMode)
    BEGIN SET @Result = -2; SET @Message = 'Theme not configured for this appearance mode.'; RETURN; END

    UPDATE Players SET AppearanceMode = @AppearanceMode WHERE PlayerId = @PlayerId;
    IF @@ROWCOUNT = 0
    BEGIN SET @Result = -3; SET @Message = 'Player not found.'; RETURN; END

    SET @Result = 0;
    SET @Message = 'Appearance updated.';
END
GO

-- Seed themes (idempotent)
IF NOT EXISTS (SELECT 1 FROM AppThemes WHERE ThemeCode = 'dark_premium')
    INSERT INTO AppThemes (ThemeCode, ThemeName, AppearanceMode, IsActive, SortOrder,
        Bg, Surface, Card, Border, Gold, GoldDim, Accent, AccentBright, Green, Red, Orange,
        TextColor, TextMuted, TextDim, WalletTop, WalletBottom, FreeModeTop, FreeModeBottom, PremiumTop, PremiumBottom)
    VALUES ('dark_premium', 'Dark Premium', 'Dark', 1, 1,
        '#0A0A0F', '#12121A', '#1A1A26', '#2A2A3E', '#FFD700', '#B8960C', '#7C3AED', '#9F67FF',
        '#22C55E', '#EF4444', '#F97316', '#F0F0FF', '#8888AA', '#555570',
        '#1A0A3E', '#0A0A1E', '#0A2A0A', '#0A1A0A', '#1A0A3E', '#0A0A2A');
GO

IF NOT EXISTS (SELECT 1 FROM AppThemes WHERE ThemeCode = 'light_white')
    INSERT INTO AppThemes (ThemeCode, ThemeName, AppearanceMode, IsActive, SortOrder,
        Bg, Surface, Card, Border, Gold, GoldDim, Accent, AccentBright, Green, Red, Orange,
        TextColor, TextMuted, TextDim, WalletTop, WalletBottom, FreeModeTop, FreeModeBottom, PremiumTop, PremiumBottom)
    VALUES ('light_white', 'Pure Light', 'Light', 0, 2,
        '#FFFFFF', '#F5F5F7', '#EEEEEE', '#D1D5DB', '#1D4ED8', '#1E40AF', '#6366F1', '#818CF8',
        '#16A34A', '#DC2626', '#EA580C', '#111827', '#6B7280', '#9CA3AF',
        '#EEF2FF', '#F9FAFB', '#ECFDF5', '#F0FDF4', '#EDE9FE', '#F5F3FF');
GO

IF NOT EXISTS (SELECT 1 FROM AppThemes WHERE ThemeCode = 'dark_blue')
    INSERT INTO AppThemes (ThemeCode, ThemeName, AppearanceMode, IsActive, SortOrder,
        Bg, Surface, Card, Border, Gold, GoldDim, Accent, AccentBright, Green, Red, Orange,
        TextColor, TextMuted, TextDim, WalletTop, WalletBottom, FreeModeTop, FreeModeBottom, PremiumTop, PremiumBottom)
    VALUES ('dark_blue', 'Dark Blue', 'Dark', 0, 3,
        '#0B1120', '#111827', '#1E293B', '#334155', '#38BDF8', '#0284C7', '#4F46E5', '#6366F1',
        '#34D399', '#FB7185', '#FB923C', '#F1F5F9', '#94A3B8', '#64748B',
        '#0F172A', '#020617', '#064E3B', '#022C22', '#1E1B4B', '#0F172A');
GO

IF NOT EXISTS (SELECT 1 FROM AppThemes WHERE ThemeCode = 'soft_light')
    INSERT INTO AppThemes (ThemeCode, ThemeName, AppearanceMode, IsActive, SortOrder,
        Bg, Surface, Card, Border, Gold, GoldDim, Accent, AccentBright, Green, Red, Orange,
        TextColor, TextMuted, TextDim, WalletTop, WalletBottom, FreeModeTop, FreeModeBottom, PremiumTop, PremiumBottom)
    VALUES ('soft_light', 'Soft Light', 'Light', 0, 4,
        '#FAFAF8', '#F0F0EB', '#E8E8E2', '#D6D3D1', '#0D9488', '#0F766E', '#D97706', '#F59E0B',
        '#059669', '#E11D48', '#EA580C', '#1C1917', '#78716C', '#A8A29E',
        '#CCFBF1', '#F0FDFA', '#D1FAE5', '#ECFDF5', '#FFEDD5', '#FFF7ED');
GO

-- Dashboard proc: include AppearanceMode
CREATE OR ALTER PROCEDURE USP_GetPlayerDashboard
    @PlayerId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT  P.PlayerId, P.ContactEmail, P.ContactPhone, P.Username, P.StateCode, P.ReferralCode,
            P.AppearanceMode,
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

PRINT 'SpeedSaga DB update 020 applied.';
GO
