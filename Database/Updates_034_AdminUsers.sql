USE SpeedSagaDB;
GO
SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID('dbo.AdminUsers', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AdminUsers (
        AdminUserId   UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_AdminUsers PRIMARY KEY DEFAULT NEWID(),
        Email         NVARCHAR(120) NULL,
        Phone         NVARCHAR(20) NULL,
        DisplayName   NVARCHAR(80) NOT NULL,
        PasswordHash  NVARCHAR(128) NOT NULL,
        PasswordSalt  NVARCHAR(64) NOT NULL,
        Role          NVARCHAR(20) NOT NULL,
        IsActive      BIT NOT NULL CONSTRAINT DF_AdminUsers_IsActive DEFAULT (1),
        CreatedAt     DATETIME2 NOT NULL CONSTRAINT DF_AdminUsers_CreatedAt DEFAULT (SYSUTCDATETIME()),
        LastLoginAt   DATETIME2 NULL,
        CONSTRAINT CK_AdminUsers_Role CHECK (Role IN ('SuperAdmin', 'Support')),
        CONSTRAINT CK_AdminUsers_Contact CHECK (Email IS NOT NULL OR Phone IS NOT NULL)
    );

    CREATE UNIQUE INDEX UX_AdminUsers_Email ON dbo.AdminUsers(Email) WHERE Email IS NOT NULL;
    CREATE UNIQUE INDEX UX_AdminUsers_Phone ON dbo.AdminUsers(Phone) WHERE Phone IS NOT NULL;
END
GO

CREATE OR ALTER PROCEDURE dbo.USP_AdminLogin
    @Contact NVARCHAR(150)
AS
BEGIN
    SET NOCOUNT ON;
    SET @Contact = LTRIM(RTRIM(@Contact));

    SELECT TOP 1
        AdminUserId,
        Email,
        Phone,
        DisplayName,
        PasswordHash,
        PasswordSalt,
        Role,
        IsActive
    FROM dbo.AdminUsers
    WHERE IsActive = 1
      AND (Email = @Contact OR Phone = @Contact);
END
GO

CREATE OR ALTER PROCEDURE dbo.USP_AdminTouchLogin
    @AdminUserId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.AdminUsers
    SET LastLoginAt = SYSUTCDATETIME()
    WHERE AdminUserId = @AdminUserId;
END
GO

CREATE OR ALTER PROCEDURE dbo.USP_AdminCreateUser
    @Email NVARCHAR(120) = NULL,
    @Phone NVARCHAR(20) = NULL,
    @DisplayName NVARCHAR(80),
    @PasswordHash NVARCHAR(128),
    @PasswordSalt NVARCHAR(64),
    @Role NVARCHAR(20),
    @AdminUserId UNIQUEIDENTIFIER OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    IF @Role NOT IN ('SuperAdmin', 'Support')
    BEGIN
        RAISERROR('Invalid admin role.', 16, 1);
        RETURN;
    END

    SET @AdminUserId = NEWID();
    INSERT INTO dbo.AdminUsers (AdminUserId, Email, Phone, DisplayName, PasswordHash, PasswordSalt, Role)
    VALUES (@AdminUserId, @Email, @Phone, @DisplayName, @PasswordHash, @PasswordSalt, @Role);
END
GO

CREATE OR ALTER PROCEDURE dbo.USP_AdminCountUsers
AS
BEGIN
    SET NOCOUNT ON;
    SELECT COUNT(*) AS UserCount FROM dbo.AdminUsers;
END
GO

PRINT 'Updates 034 applied: AdminUsers table and login procedures.';
GO
