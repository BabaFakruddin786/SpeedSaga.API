-- ============================================================
-- SpeedSaga — default admin users (production seed)
-- Run after all migrations. Safe to re-run (skips if users exist).
-- ============================================================
USE SpeedSagaDB;
GO

IF OBJECT_ID('dbo.AdminUsers', 'U') IS NULL
BEGIN
    RAISERROR('AdminUsers table missing. Run Updates_034_AdminUsers.sql first.', 16, 1);
    RETURN;
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.AdminUsers)
BEGIN
    INSERT INTO dbo.AdminUsers (AdminUserId, Email, Phone, DisplayName, PasswordHash, PasswordSalt, Role, IsActive)
    VALUES
    (
        NEWID(),
        N'admin@speedsaga.com',
        N'9999999999',
        N'Super Admin',
        N'j+6YFI0RH44sNaOeHMJ3ht7JWIzXXZ02lUVpy8u2/jw=',
        N'SpeedSagaProdAdminSalt00000001==',
        N'SuperAdmin',
        1
    ),
    (
        NEWID(),
        N'support@speedsaga.com',
        N'8888888888',
        N'Support Agent',
        N'mplQbQoAXFOlHxNOZ1NP+5LSIx+Kr7/h8vZEmTEEjpo=',
        N'SpeedSagaProdSupportSalt00002==',
        N'Support',
        1
    );

    PRINT 'Seeded default admin users.';
END
ELSE
    PRINT 'Admin users already exist — seed skipped.';
GO
