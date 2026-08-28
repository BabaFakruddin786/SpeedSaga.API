-- Profile/KYC completion: bank proof, rejection reasons, admin review helpers
USE SpeedSagaDB;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH('PlayerKYC', 'BankDocPath') IS NULL
    ALTER TABLE PlayerKYC ADD BankDocPath NVARCHAR(260) NULL;
GO

IF COL_LENGTH('PlayerKYC', 'AadhaarRejectReason') IS NULL
    ALTER TABLE PlayerKYC ADD AadhaarRejectReason NVARCHAR(300) NULL;
GO

IF COL_LENGTH('PlayerKYC', 'PanRejectReason') IS NULL
    ALTER TABLE PlayerKYC ADD PanRejectReason NVARCHAR(300) NULL;
GO

IF COL_LENGTH('PlayerKYC', 'BankRejectReason') IS NULL
    ALTER TABLE PlayerKYC ADD BankRejectReason NVARCHAR(300) NULL;
GO

CREATE OR ALTER PROCEDURE USP_SetKycDocument
    @PlayerId        UNIQUEIDENTIFIER,
    @DocType         NVARCHAR(20),
    @DocNumber       NVARCHAR(100) = NULL,
    @HolderName      NVARCHAR(100) = NULL,
    @Ifsc            NVARCHAR(15) = NULL,
    @Status          NVARCHAR(20) = 'PendingReview',
    @NameOnAadhaar   NVARCHAR(100) = NULL,
    @DocPath         NVARCHAR(260) = NULL,
    @RejectReason    NVARCHAR(300) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM PlayerKYC WHERE PlayerId = @PlayerId)
        INSERT INTO PlayerKYC (PlayerId) VALUES (@PlayerId);

    IF @DocType = 'Aadhaar'
        UPDATE PlayerKYC SET
            AadhaarStatus = @Status,
            AadhaarNumber = COALESCE(@DocNumber, AadhaarNumber),
            AadhaarNameOnCard = COALESCE(@NameOnAadhaar, AadhaarNameOnCard),
            AadhaarDocPath = COALESCE(@DocPath, AadhaarDocPath),
            AadhaarRejectReason = CASE WHEN @Status = 'Rejected' THEN @RejectReason ELSE NULL END,
            UpdatedAt = GETDATE()
        WHERE PlayerId = @PlayerId;
    ELSE IF @DocType = 'PAN'
        UPDATE PlayerKYC SET
            PANStatus = @Status,
            PANNumber = COALESCE(@DocNumber, PANNumber),
            PanDocPath = COALESCE(@DocPath, PanDocPath),
            PanRejectReason = CASE WHEN @Status = 'Rejected' THEN @RejectReason ELSE NULL END,
            UpdatedAt = GETDATE()
        WHERE PlayerId = @PlayerId;
    ELSE IF @DocType = 'Bank'
        UPDATE PlayerKYC SET
            BankStatus = @Status,
            BankAccount = COALESCE(@DocNumber, BankAccount),
            BankName = COALESCE(@HolderName, BankName),
            BankIFSC = COALESCE(@Ifsc, BankIFSC),
            BankDocPath = COALESCE(@DocPath, BankDocPath),
            BankRejectReason = CASE WHEN @Status = 'Rejected' THEN @RejectReason ELSE NULL END,
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
           AadhaarNameOnCard,
           AadhaarRejectReason,
           PanRejectReason,
           BankRejectReason
    FROM PlayerKYC WHERE PlayerId = @PlayerId;
END
GO

CREATE OR ALTER PROCEDURE USP_AdminListPendingKyc
    @PageNo   INT = 1,
    @PageSize INT = 50
AS
BEGIN
    SET NOCOUNT ON;
    IF @PageNo < 1 SET @PageNo = 1;
    IF @PageSize < 1 OR @PageSize > 200 SET @PageSize = 50;

    SELECT k.PlayerId, p.Username, p.ContactEmail, p.ContactPhone,
           k.AadhaarStatus, k.PANStatus, k.BankStatus, k.IsFullyVerified,
           k.AadhaarNumber, k.PANNumber, k.BankAccount, k.BankIFSC, k.BankName,
           k.AadhaarNameOnCard, k.UpdatedAt
    FROM PlayerKYC k
    INNER JOIN Players p ON p.PlayerId = k.PlayerId
    WHERE k.AadhaarStatus = 'PendingReview'
       OR k.PANStatus = 'PendingReview'
       OR k.BankStatus = 'PendingReview'
    ORDER BY k.UpdatedAt DESC
    OFFSET (@PageNo - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
END
GO

PRINT 'Updates 029 applied: bank proof, reject reasons, admin pending KYC list.';
GO
