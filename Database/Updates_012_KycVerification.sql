-- KYC verification: set document status and auto-complete when all three are Verified
CREATE OR ALTER PROCEDURE USP_SetKycDocument
    @PlayerId   UNIQUEIDENTIFIER,
    @DocType    NVARCHAR(20),
    @DocNumber  NVARCHAR(100) = NULL,
    @HolderName NVARCHAR(100) = NULL,
    @Ifsc       NVARCHAR(15) = NULL,
    @Status     NVARCHAR(20) = 'Verified'
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM PlayerKYC WHERE PlayerId = @PlayerId)
        INSERT INTO PlayerKYC (PlayerId) VALUES (@PlayerId);

    IF @DocType = 'Aadhaar'
        UPDATE PlayerKYC SET AadhaarStatus = @Status, AadhaarNumber = @DocNumber, UpdatedAt = GETDATE()
        WHERE PlayerId = @PlayerId;
    ELSE IF @DocType = 'PAN'
        UPDATE PlayerKYC SET PANStatus = @Status, PANNumber = @DocNumber, UpdatedAt = GETDATE()
        WHERE PlayerId = @PlayerId;
    ELSE IF @DocType = 'Bank'
        UPDATE PlayerKYC SET BankStatus = @Status, BankAccount = @DocNumber, BankName = @HolderName,
            BankIFSC = @Ifsc, UpdatedAt = GETDATE()
        WHERE PlayerId = @PlayerId;

    UPDATE PlayerKYC SET IsFullyVerified = CASE
        WHEN AadhaarStatus IN ('Verified', 'Approved')
         AND PANStatus IN ('Verified', 'Approved')
         AND BankStatus IN ('Verified', 'Approved') THEN 1
        ELSE 0 END
    WHERE PlayerId = @PlayerId;
END
GO
