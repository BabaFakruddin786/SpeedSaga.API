USE SpeedSagaDB;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- ── Payment gateway config (single row) ─────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AppPaymentConfig')
BEGIN
    CREATE TABLE dbo.AppPaymentConfig (
        ConfigId                INT             NOT NULL CONSTRAINT PK_AppPaymentConfig PRIMARY KEY,
        IsRazorpayEnabled       BIT             NOT NULL CONSTRAINT DF_AppPaymentConfig_Rzp DEFAULT 1,
        RazorpayKeyId           NVARCHAR(200)   NOT NULL CONSTRAINT DF_AppPaymentConfig_KeyId DEFAULT N'',
        RazorpayKeySecret       NVARCHAR(500)   NOT NULL CONSTRAINT DF_AppPaymentConfig_Secret DEFAULT N'',
        RazorpayWebhookSecret   NVARCHAR(500)   NOT NULL CONSTRAINT DF_AppPaymentConfig_Webhook DEFAULT N'',
        CompanyBankName         NVARCHAR(200)   NULL,
        CompanyBankAccount      NVARCHAR(50)    NULL,
        CompanyBankIfsc         NVARCHAR(20)    NULL,
        CompanyBankHolder       NVARCHAR(200)   NULL,
        MinDepositPaise         BIGINT          NOT NULL CONSTRAINT DF_AppPaymentConfig_MinDep DEFAULT 10000,
        MinWithdrawPaise        BIGINT          NOT NULL CONSTRAINT DF_AppPaymentConfig_MinWdr DEFAULT 10000,
        MaxWithdrawPaise        BIGINT          NOT NULL CONSTRAINT DF_AppPaymentConfig_MaxWdr DEFAULT 50000000,
        FcmServerKey            NVARCHAR(500)   NULL,
        IsPushEnabled           BIT             NOT NULL CONSTRAINT DF_AppPaymentConfig_Push DEFAULT 0,
        UpdatedAt               DATETIME2       NOT NULL CONSTRAINT DF_AppPaymentConfig_Updated DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_AppPaymentConfig_SingleRow CHECK (ConfigId = 1)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.AppPaymentConfig WHERE ConfigId = 1)
BEGIN
    INSERT INTO dbo.AppPaymentConfig (ConfigId, IsRazorpayEnabled, RazorpayKeyId, RazorpayKeySecret, RazorpayWebhookSecret)
    VALUES (1, 1, N'', N'', N'');
END
GO

CREATE OR ALTER PROCEDURE dbo.USP_GetAppPaymentConfig
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP 1
        IsRazorpayEnabled,
        RazorpayKeyId,
        RazorpayKeySecret,
        RazorpayWebhookSecret,
        CompanyBankName,
        CompanyBankAccount,
        CompanyBankIfsc,
        CompanyBankHolder,
        MinDepositPaise,
        MinWithdrawPaise,
        MaxWithdrawPaise,
        FcmServerKey,
        IsPushEnabled,
        UpdatedAt
    FROM dbo.AppPaymentConfig
    WHERE ConfigId = 1;
END
GO

CREATE OR ALTER PROCEDURE dbo.USP_UpdateAppPaymentConfig
    @IsRazorpayEnabled       BIT,
    @RazorpayKeyId           NVARCHAR(200),
    @RazorpayKeySecret       NVARCHAR(500),
    @RazorpayWebhookSecret   NVARCHAR(500),
    @CompanyBankName         NVARCHAR(200) = NULL,
    @CompanyBankAccount      NVARCHAR(50)  = NULL,
    @CompanyBankIfsc         NVARCHAR(20)  = NULL,
    @CompanyBankHolder       NVARCHAR(200) = NULL,
    @MinDepositPaise         BIGINT,
    @MinWithdrawPaise        BIGINT,
    @MaxWithdrawPaise        BIGINT,
    @FcmServerKey            NVARCHAR(500) = NULL,
    @IsPushEnabled           BIT,
    @Result                  INT OUTPUT,
    @Message                 NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @Result = 0;
    SET @Message = N'Payment config updated';

    IF @MinDepositPaise < 1000 OR @MinWithdrawPaise < 1000
    BEGIN SET @Result = -1; SET @Message = N'Minimum amounts must be at least Rs 10.'; RETURN; END
    IF @MaxWithdrawPaise < @MinWithdrawPaise
    BEGIN SET @Result = -2; SET @Message = N'Max withdrawal must be greater than min withdrawal.'; RETURN; END

    IF NOT EXISTS (SELECT 1 FROM dbo.AppPaymentConfig WHERE ConfigId = 1)
        INSERT INTO dbo.AppPaymentConfig (ConfigId) VALUES (1);

    UPDATE dbo.AppPaymentConfig
    SET IsRazorpayEnabled = @IsRazorpayEnabled,
        RazorpayKeyId = NULLIF(LTRIM(RTRIM(@RazorpayKeyId)), N''),
        RazorpayKeySecret = CASE WHEN NULLIF(LTRIM(RTRIM(@RazorpayKeySecret)), N'') IS NULL
            THEN RazorpayKeySecret ELSE @RazorpayKeySecret END,
        RazorpayWebhookSecret = CASE WHEN NULLIF(LTRIM(RTRIM(@RazorpayWebhookSecret)), N'') IS NULL
            THEN RazorpayWebhookSecret ELSE @RazorpayWebhookSecret END,
        CompanyBankName = @CompanyBankName,
        CompanyBankAccount = @CompanyBankAccount,
        CompanyBankIfsc = @CompanyBankIfsc,
        CompanyBankHolder = @CompanyBankHolder,
        MinDepositPaise = @MinDepositPaise,
        MinWithdrawPaise = @MinWithdrawPaise,
        MaxWithdrawPaise = @MaxWithdrawPaise,
        FcmServerKey = CASE WHEN NULLIF(LTRIM(RTRIM(@FcmServerKey)), N'') IS NULL
            THEN FcmServerKey ELSE @FcmServerKey END,
        IsPushEnabled = @IsPushEnabled,
        UpdatedAt = SYSUTCDATETIME()
    WHERE ConfigId = 1;
END
GO

-- ── Theme admin ─────────────────────────────────────────────────────────────
CREATE OR ALTER PROCEDURE dbo.USP_AdminListThemes
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        ThemeId, ThemeCode, ThemeName, AppearanceMode, IsActive, SortOrder,
        Bg, Surface, Card, Border, Gold, GoldDim, Accent, AccentBright,
        Green, Red, Orange, TextColor, TextMuted, TextDim,
        WalletTop, WalletBottom, FreeModeTop, FreeModeBottom, PremiumTop, PremiumBottom,
        UpdatedAt
    FROM dbo.AppThemes
    ORDER BY SortOrder, ThemeId;
END
GO

CREATE OR ALTER PROCEDURE dbo.USP_AdminUpdateTheme
    @ThemeCode       NVARCHAR(50),
    @ThemeName       NVARCHAR(100),
    @SortOrder       INT,
    @Bg              NVARCHAR(10),
    @Surface         NVARCHAR(10),
    @Card            NVARCHAR(10),
    @Border          NVARCHAR(10),
    @Gold            NVARCHAR(10),
    @GoldDim         NVARCHAR(10),
    @Accent          NVARCHAR(10),
    @AccentBright    NVARCHAR(10),
    @Green           NVARCHAR(10),
    @Red             NVARCHAR(10),
    @Orange          NVARCHAR(10),
    @TextColor       NVARCHAR(10),
    @TextMuted       NVARCHAR(10),
    @TextDim         NVARCHAR(10),
    @WalletTop       NVARCHAR(10),
    @WalletBottom    NVARCHAR(10),
    @FreeModeTop     NVARCHAR(10),
    @FreeModeBottom  NVARCHAR(10),
    @PremiumTop      NVARCHAR(10),
    @PremiumBottom   NVARCHAR(10),
    @Result          INT OUTPUT,
    @Message         NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @Result = 0;
    SET @Message = N'Theme updated';

    IF NOT EXISTS (SELECT 1 FROM dbo.AppThemes WHERE ThemeCode = @ThemeCode)
    BEGIN SET @Result = -1; SET @Message = N'Theme not found.'; RETURN; END

    UPDATE dbo.AppThemes
    SET ThemeName = @ThemeName,
        SortOrder = @SortOrder,
        Bg = @Bg, Surface = @Surface, Card = @Card, Border = @Border,
        Gold = @Gold, GoldDim = @GoldDim, Accent = @Accent, AccentBright = @AccentBright,
        Green = @Green, Red = @Red, Orange = @Orange,
        TextColor = @TextColor, TextMuted = @TextMuted, TextDim = @TextDim,
        WalletTop = @WalletTop, WalletBottom = @WalletBottom,
        FreeModeTop = @FreeModeTop, FreeModeBottom = @FreeModeBottom,
        PremiumTop = @PremiumTop, PremiumBottom = @PremiumBottom,
        UpdatedAt = SYSUTCDATETIME()
    WHERE ThemeCode = @ThemeCode;
END
GO

CREATE OR ALTER PROCEDURE dbo.USP_AdminSetActiveTheme
    @ThemeCode   NVARCHAR(50),
    @Result      INT OUTPUT,
    @Message     NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @Result = 0;
    SET @Message = N'Default theme updated';

    IF NOT EXISTS (SELECT 1 FROM dbo.AppThemes WHERE ThemeCode = @ThemeCode)
    BEGIN SET @Result = -1; SET @Message = N'Theme not found.'; RETURN; END

    UPDATE dbo.AppThemes SET IsActive = 0;
    UPDATE dbo.AppThemes SET IsActive = 1, UpdatedAt = SYSUTCDATETIME() WHERE ThemeCode = @ThemeCode;
END
GO

-- ── Withdrawal approval ───────────────────────────────────────────────────────
CREATE OR ALTER PROCEDURE dbo.USP_AdminListPendingWithdrawals
    @PageNo     INT = 1,
    @PageSize   INT = 50
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        T.TxnId,
        T.PlayerId,
        P.Username,
        P.ContactEmail,
        P.ContactPhone,
        T.AmountPaise,
        T.BalanceAfter,
        T.Status,
        T.Remarks,
        T.CreatedAt,
        K.BankName AS BankHolder,
        K.BankAccount,
        K.BankIFSC AS BankIfsc
    FROM dbo.Transactions T
    INNER JOIN dbo.Players P ON P.PlayerId = T.PlayerId
    LEFT JOIN dbo.PlayerKYC K ON K.PlayerId = T.PlayerId
    WHERE T.TxnType = N'Withdrawal' AND T.Status = N'Pending'
    ORDER BY T.CreatedAt ASC
    OFFSET (@PageNo - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
END
GO

CREATE OR ALTER PROCEDURE dbo.USP_AdminProcessWithdrawal
    @TxnId          UNIQUEIDENTIFIER,
    @Action         NVARCHAR(20),
    @GatewayRef     NVARCHAR(200) = NULL,
    @Remarks        NVARCHAR(500) = NULL,
    @Result         INT OUTPUT,
    @Message        NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @Result = 0;

    DECLARE @PlayerId UNIQUEIDENTIFIER, @Amount BIGINT, @Status NVARCHAR(20);

    SELECT @PlayerId = PlayerId, @Amount = AmountPaise, @Status = Status
    FROM dbo.Transactions
    WHERE TxnId = @TxnId AND TxnType = N'Withdrawal';

    IF @PlayerId IS NULL BEGIN SET @Result = -1; SET @Message = N'Withdrawal not found.'; RETURN; END
    IF @Status <> N'Pending' BEGIN SET @Result = -2; SET @Message = N'Withdrawal is not pending.'; RETURN; END

    IF @Action = N'Approve'
    BEGIN
        UPDATE dbo.Transactions
        SET Status = N'Success',
            Gateway = N'Manual',
            GatewayRef = @GatewayRef,
            Remarks = COALESCE(@Remarks, N'Withdrawal approved by admin'),
            UpdatedAt = GETDATE()
        WHERE TxnId = @TxnId;
        SET @Message = N'Withdrawal approved.';
        RETURN;
    END

    IF @Action = N'Reject'
    BEGIN
        BEGIN TRANSACTION;
        BEGIN TRY
            UPDATE dbo.Wallets
            SET BalancePaise += @Amount,
                WithdrawnPaise -= @Amount,
                UpdatedAt = GETDATE()
            WHERE PlayerId = @PlayerId;

            UPDATE dbo.Transactions
            SET Status = N'Failed',
                Remarks = COALESCE(@Remarks, N'Withdrawal rejected by admin'),
                UpdatedAt = GETDATE()
            WHERE TxnId = @TxnId;

            COMMIT;
            SET @Message = N'Withdrawal rejected and amount refunded to wallet.';
        END TRY
        BEGIN CATCH
            ROLLBACK;
            SET @Result = -99;
            SET @Message = ERROR_MESSAGE();
        END CATCH
        RETURN;
    END

    SET @Result = -3;
    SET @Message = N'Invalid action. Use Approve or Reject.';
END
GO

-- ── Admin notifications ───────────────────────────────────────────────────────
CREATE OR ALTER PROCEDURE dbo.USP_AdminBroadcastNotification
    @Title          NVARCHAR(200),
    @Body           NVARCHAR(1000),
    @NotifType      NVARCHAR(50) = N'System',
    @NotifId        UNIQUEIDENTIFIER OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @NotifId = NEWID();
    INSERT INTO dbo.Notifications (NotifId, PlayerId, Title, Body, NotifType, FCMSent)
    VALUES (@NotifId, NULL, @Title, @Body, @NotifType, 0);
END
GO

CREATE OR ALTER PROCEDURE dbo.USP_AdminSendPlayerNotification
    @PlayerId       UNIQUEIDENTIFIER,
    @Title          NVARCHAR(200),
    @Body           NVARCHAR(1000),
    @NotifType      NVARCHAR(50) = N'System',
    @NotifId        UNIQUEIDENTIFIER OUTPUT,
    @Result         INT OUTPUT,
    @Message        NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @Result = 0;
    SET @Message = N'Notification sent';

    IF NOT EXISTS (SELECT 1 FROM dbo.Players WHERE PlayerId = @PlayerId)
    BEGIN SET @Result = -1; SET @Message = N'Player not found.'; RETURN; END

    SET @NotifId = NEWID();
    INSERT INTO dbo.Notifications (NotifId, PlayerId, Title, Body, NotifType, FCMSent)
    VALUES (@NotifId, @PlayerId, @Title, @Body, @NotifType, 0);
END
GO

CREATE OR ALTER PROCEDURE dbo.USP_AdminListNotifications
    @PageNo     INT = 1,
    @PageSize   INT = 50
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        N.NotifId,
        N.PlayerId,
        P.Username,
        P.ContactPhone,
        P.ContactEmail,
        N.Title,
        N.Body,
        N.NotifType,
        N.FCMSent,
        N.CreatedAt,
        CASE WHEN N.PlayerId IS NULL THEN N'Broadcast' ELSE N'Player' END AS Audience
    FROM dbo.Notifications N
    LEFT JOIN dbo.Players P ON P.PlayerId = N.PlayerId
    ORDER BY N.CreatedAt DESC
    OFFSET (@PageNo - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'PlayerDeviceTokens')
BEGIN
    CREATE TABLE dbo.PlayerDeviceTokens (
        TokenId     UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_PlayerDeviceTokens PRIMARY KEY DEFAULT NEWID(),
        PlayerId    UNIQUEIDENTIFIER NOT NULL,
        DeviceToken NVARCHAR(500)    NOT NULL,
        Platform    NVARCHAR(20)     NOT NULL,
        UpdatedAt   DATETIME2        NOT NULL CONSTRAINT DF_PlayerDeviceTokens_Updated DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_PlayerDeviceTokens_Player FOREIGN KEY (PlayerId) REFERENCES dbo.Players(PlayerId)
    );
    CREATE UNIQUE INDEX UX_PlayerDeviceTokens_Token ON dbo.PlayerDeviceTokens(DeviceToken);
    CREATE INDEX IX_PlayerDeviceTokens_Player ON dbo.PlayerDeviceTokens(PlayerId);
END
GO

CREATE OR ALTER PROCEDURE dbo.USP_RegisterPlayerDeviceToken
    @PlayerId       UNIQUEIDENTIFIER,
    @DeviceToken    NVARCHAR(500),
    @Platform       NVARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM dbo.PlayerDeviceTokens WHERE DeviceToken = @DeviceToken)
        UPDATE dbo.PlayerDeviceTokens
        SET PlayerId = @PlayerId, Platform = @Platform, UpdatedAt = SYSUTCDATETIME()
        WHERE DeviceToken = @DeviceToken;
    ELSE
        INSERT INTO dbo.PlayerDeviceTokens (PlayerId, DeviceToken, Platform)
        VALUES (@PlayerId, @DeviceToken, @Platform);
END
GO

PRINT 'Updates_035_AdminPhase2 applied.';
GO
