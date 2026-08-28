USE SpeedSagaDB;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AppSupportConfig')
BEGIN
    CREATE TABLE dbo.AppSupportConfig (
        ConfigId            INT             NOT NULL CONSTRAINT PK_AppSupportConfig PRIMARY KEY,
        SupportEmail        NVARCHAR(200)   NOT NULL,
        SupportPhone        NVARCHAR(20)    NOT NULL,
        SupportWhatsApp     NVARCHAR(20)    NOT NULL,
        SupportHoursLine    NVARCHAR(300)   NOT NULL,
        SupportHoursJson    NVARCHAR(MAX)   NOT NULL,
        SupportFaqJson      NVARCHAR(MAX)   NOT NULL,
        UpdatedAt           DATETIME2       NOT NULL CONSTRAINT DF_AppSupportConfig_UpdatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_AppSupportConfig_SingleRow CHECK (ConfigId = 1)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.AppSupportConfig WHERE ConfigId = 1)
BEGIN
    INSERT INTO dbo.AppSupportConfig (ConfigId, SupportEmail, SupportPhone, SupportWhatsApp, SupportHoursLine, SupportHoursJson, SupportFaqJson)
    VALUES (
        1,
        N'support@speedsaga.com',
        N'9052916052',
        N'9052916052',
        N'Mon–Sat 10 AM – 7 PM IST',
        N'["Mon–Sat · 10:00 AM – 7:00 PM IST","Sunday & public holidays · email only","WhatsApp usually replies within 2 hours on business days"]',
        N'["Withdrawal pending? Complete Account Verification first.","Deposit missing? Wait 10 minutes, then email us with payment ID.","Game disconnected? Two-player forfeit rules apply after timeout.","Forgot password? Use Forgot Password on the login screen."]'
    );
END
GO

CREATE OR ALTER PROCEDURE dbo.USP_GetAppSupportConfig
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP 1
        SupportEmail,
        SupportPhone,
        SupportWhatsApp,
        SupportHoursLine,
        SupportHoursJson,
        SupportFaqJson,
        UpdatedAt
    FROM dbo.AppSupportConfig
    WHERE ConfigId = 1;
END
GO

CREATE OR ALTER PROCEDURE dbo.USP_UpdateAppSupportConfig
    @SupportEmail     NVARCHAR(200),
    @SupportPhone     NVARCHAR(20),
    @SupportWhatsApp  NVARCHAR(20),
    @SupportHoursLine NVARCHAR(300),
    @SupportHoursJson NVARCHAR(MAX),
    @SupportFaqJson   NVARCHAR(MAX),
    @Result           INT OUTPUT,
    @Message          NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @Result = 0;
    SET @Message = N'Support config updated';

    IF NULLIF(LTRIM(RTRIM(@SupportEmail)), N'') IS NULL
    BEGIN SET @Result = -1; SET @Message = N'Support email is required.'; RETURN; END
    IF NULLIF(LTRIM(RTRIM(@SupportPhone)), N'') IS NULL
    BEGIN SET @Result = -2; SET @Message = N'Support phone is required.'; RETURN; END
    IF NULLIF(LTRIM(RTRIM(@SupportWhatsApp)), N'') IS NULL
    BEGIN SET @Result = -3; SET @Message = N'Support WhatsApp number is required.'; RETURN; END

    IF NOT EXISTS (SELECT 1 FROM dbo.AppSupportConfig WHERE ConfigId = 1)
    BEGIN
        INSERT INTO dbo.AppSupportConfig (ConfigId, SupportEmail, SupportPhone, SupportWhatsApp, SupportHoursLine, SupportHoursJson, SupportFaqJson)
        VALUES (1, @SupportEmail, @SupportPhone, @SupportWhatsApp, @SupportHoursLine, @SupportHoursJson, @SupportFaqJson);
    END
    ELSE
    BEGIN
        UPDATE dbo.AppSupportConfig
        SET SupportEmail = @SupportEmail,
            SupportPhone = @SupportPhone,
            SupportWhatsApp = @SupportWhatsApp,
            SupportHoursLine = @SupportHoursLine,
            SupportHoursJson = @SupportHoursJson,
            SupportFaqJson = @SupportFaqJson,
            UpdatedAt = SYSUTCDATETIME()
        WHERE ConfigId = 1;
    END
END
GO

PRINT 'Updates 030 applied: AppSupportConfig table and procedures.';
