-- KYC document upload + manual review (non-UIDAI flow)
IF COL_LENGTH('PlayerKYC', 'AadhaarNameOnCard') IS NULL
    ALTER TABLE PlayerKYC ADD AadhaarNameOnCard NVARCHAR(100) NULL;
GO

IF COL_LENGTH('PlayerKYC', 'AadhaarDocPath') IS NULL
    ALTER TABLE PlayerKYC ADD AadhaarDocPath NVARCHAR(260) NULL;
GO

IF COL_LENGTH('PlayerKYC', 'PanDocPath') IS NULL
    ALTER TABLE PlayerKYC ADD PanDocPath NVARCHAR(260) NULL;
GO

CREATE OR ALTER PROCEDURE USP_SetKycDocument
    @PlayerId        UNIQUEIDENTIFIER,
    @DocType         NVARCHAR(20),
    @DocNumber       NVARCHAR(100) = NULL,
    @HolderName      NVARCHAR(100) = NULL,
    @Ifsc            NVARCHAR(15) = NULL,
    @Status          NVARCHAR(20) = 'PendingReview',
    @NameOnAadhaar   NVARCHAR(100) = NULL,
    @DocPath         NVARCHAR(260) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM PlayerKYC WHERE PlayerId = @PlayerId)
        INSERT INTO PlayerKYC (PlayerId) VALUES (@PlayerId);

    IF @DocType = 'Aadhaar'
        UPDATE PlayerKYC SET
            AadhaarStatus = @Status,
            AadhaarNumber = @DocNumber,
            AadhaarNameOnCard = COALESCE(@NameOnAadhaar, AadhaarNameOnCard),
            AadhaarDocPath = COALESCE(@DocPath, AadhaarDocPath),
            UpdatedAt = GETDATE()
        WHERE PlayerId = @PlayerId;
    ELSE IF @DocType = 'PAN'
        UPDATE PlayerKYC SET
            PANStatus = @Status,
            PANNumber = @DocNumber,
            PanDocPath = COALESCE(@DocPath, PanDocPath),
            UpdatedAt = GETDATE()
        WHERE PlayerId = @PlayerId;
    ELSE IF @DocType = 'Bank'
        UPDATE PlayerKYC SET
            BankStatus = @Status,
            BankAccount = @DocNumber,
            BankName = @HolderName,
            BankIFSC = @Ifsc,
            UpdatedAt = GETDATE()
        WHERE PlayerId = @PlayerId;

    UPDATE PlayerKYC SET IsFullyVerified = CASE
        WHEN AadhaarStatus IN ('Verified', 'Approved')
         AND PANStatus IN ('Verified', 'Approved')
         AND BankStatus IN ('Verified', 'Approved') THEN 1
        ELSE 0 END
    WHERE PlayerId = @PlayerId;
END
GO

CREATE OR ALTER PROCEDURE USP_GetKycStatus
    @PlayerId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT AadhaarStatus, PANStatus, BankStatus, IsFullyVerified,
           AadhaarNumber AS AadhaarMasked,
           PANNumber AS PANMasked,
           BankAccount AS BankMasked,
           AadhaarNameOnCard
    FROM PlayerKYC WHERE PlayerId = @PlayerId;
END
GO
