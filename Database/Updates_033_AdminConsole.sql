USE SpeedSagaDB;
GO

-- Admin console: dashboard stats, finance charts, player search, global transactions
CREATE OR ALTER PROCEDURE dbo.USP_AdminDashboardStats
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Today DATE = CAST(GETDATE() AS DATE);

    SELECT
        (SELECT COUNT(*) FROM Players WHERE IsActive = 1) AS TotalPlayers,
        (SELECT COUNT(*) FROM Players WHERE LastLoginAt >= DATEADD(DAY, -7, GETDATE())) AS ActivePlayers7d,
        (SELECT ISNULL(SUM(BalancePaise), 0) FROM Wallets) AS TotalWalletBalancePaise,
        (SELECT ISNULL(SUM(DepositPaise), 0) FROM Wallets) AS TotalDepositedAllTimePaise,
        (SELECT ISNULL(SUM(WithdrawnPaise), 0) FROM Wallets) AS TotalWithdrawnAllTimePaise,
        (SELECT ISNULL(SUM(AmountPaise), 0) FROM Transactions
            WHERE TxnType = 'Deposit' AND Status = 'Success' AND CAST(CreatedAt AS DATE) = @Today) AS DepositsTodayPaise,
        (SELECT ISNULL(SUM(AmountPaise), 0) FROM Transactions
            WHERE TxnType = 'Withdrawal' AND Status = 'Success' AND CAST(CreatedAt AS DATE) = @Today) AS WithdrawalsTodayPaise,
        (SELECT ISNULL(SUM(AmountPaise), 0) FROM Transactions
            WHERE TxnType = 'EntryFee' AND Status = 'Success' AND CAST(CreatedAt AS DATE) = @Today) AS EntryFeesTodayPaise,
        (SELECT ISNULL(SUM(AmountPaise), 0) FROM Transactions
            WHERE TxnType = 'Reward' AND Status = 'Success' AND CAST(CreatedAt AS DATE) = @Today) AS RewardsTodayPaise,
        (SELECT COUNT(*) FROM PlayerKYC
            WHERE AadhaarStatus = 'PendingReview' OR PANStatus = 'PendingReview' OR BankStatus = 'PendingReview') AS PendingKycReviews,
        (SELECT COUNT(*) FROM SupportTickets WHERE Status IN ('Open', 'AwaitingAgent')) AS OpenSupportTickets,
        (SELECT COUNT(*) FROM Transactions WHERE TxnType = 'Deposit' AND Status = 'Success') AS DepositCountAllTime,
        (SELECT COUNT(*) FROM Transactions WHERE TxnType = 'Withdrawal' AND Status = 'Success') AS WithdrawalCountAllTime;
END
GO

CREATE OR ALTER PROCEDURE dbo.USP_AdminFinanceDailyFlow
    @Days INT = 30
AS
BEGIN
    SET NOCOUNT ON;
    IF @Days < 1 SET @Days = 1;
    IF @Days > 365 SET @Days = 365;

    ;WITH Dates AS (
        SELECT CAST(DATEADD(DAY, -(@Days - 1), CAST(GETDATE() AS DATE)) AS DATE) AS FlowDate
        UNION ALL
        SELECT DATEADD(DAY, 1, FlowDate) FROM Dates WHERE FlowDate < CAST(GETDATE() AS DATE)
    )
    SELECT
        d.FlowDate AS [Date],
        ISNULL(dep.AmountPaise, 0) AS DepositsPaise,
        ISNULL(wdr.AmountPaise, 0) AS WithdrawalsPaise,
        ISNULL(ent.AmountPaise, 0) AS EntryFeesPaise,
        ISNULL(rwd.AmountPaise, 0) AS RewardsPaise,
        ISNULL(dep.AmountPaise, 0) - ISNULL(wdr.AmountPaise, 0) AS NetDepositsPaise
    FROM Dates d
    LEFT JOIN (
        SELECT CAST(CreatedAt AS DATE) AS D, SUM(AmountPaise) AS AmountPaise
        FROM Transactions WHERE TxnType = 'Deposit' AND Status = 'Success'
        GROUP BY CAST(CreatedAt AS DATE)
    ) dep ON dep.D = d.FlowDate
    LEFT JOIN (
        SELECT CAST(CreatedAt AS DATE) AS D, SUM(AmountPaise) AS AmountPaise
        FROM Transactions WHERE TxnType = 'Withdrawal' AND Status = 'Success'
        GROUP BY CAST(CreatedAt AS DATE)
    ) wdr ON wdr.D = d.FlowDate
    LEFT JOIN (
        SELECT CAST(CreatedAt AS DATE) AS D, SUM(AmountPaise) AS AmountPaise
        FROM Transactions WHERE TxnType = 'EntryFee' AND Status = 'Success'
        GROUP BY CAST(CreatedAt AS DATE)
    ) ent ON ent.D = d.FlowDate
    LEFT JOIN (
        SELECT CAST(CreatedAt AS DATE) AS D, SUM(AmountPaise) AS AmountPaise
        FROM Transactions WHERE TxnType = 'Reward' AND Status = 'Success'
        GROUP BY CAST(CreatedAt AS DATE)
    ) rwd ON rwd.D = d.FlowDate
    ORDER BY d.FlowDate
    OPTION (MAXRECURSION 400);
END
GO

CREATE OR ALTER PROCEDURE dbo.USP_AdminFinanceByType
    @FromDate DATE = NULL,
    @ToDate DATE = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @FromDate IS NULL SET @FromDate = DATEADD(DAY, -30, CAST(GETDATE() AS DATE));
    IF @ToDate IS NULL SET @ToDate = CAST(GETDATE() AS DATE);

    SELECT TxnType, COUNT(*) AS TxnCount, SUM(AmountPaise) AS TotalPaise
    FROM Transactions
    WHERE Status = 'Success'
      AND CAST(CreatedAt AS DATE) BETWEEN @FromDate AND @ToDate
    GROUP BY TxnType
    ORDER BY TotalPaise DESC;
END
GO

CREATE OR ALTER PROCEDURE dbo.USP_AdminTopDepositors
    @Days INT = 30,
    @TopN INT = 10
AS
BEGIN
    SET NOCOUNT ON;
    IF @Days < 1 SET @Days = 30;
    IF @TopN < 1 SET @TopN = 10;
    IF @TopN > 50 SET @TopN = 50;

    SELECT TOP (@TopN)
        T.PlayerId,
        P.Username,
        P.ContactEmail,
        P.ContactPhone,
        SUM(T.AmountPaise) AS TotalDepositsPaise,
        COUNT(*) AS DepositCount,
        MAX(T.CreatedAt) AS LastDepositAt
    FROM Transactions T
    INNER JOIN Players P ON P.PlayerId = T.PlayerId
    WHERE T.TxnType = 'Deposit' AND T.Status = 'Success'
      AND T.CreatedAt >= DATEADD(DAY, -@Days, GETDATE())
    GROUP BY T.PlayerId, P.Username, P.ContactEmail, P.ContactPhone
    ORDER BY SUM(T.AmountPaise) DESC;
END
GO

CREATE OR ALTER PROCEDURE dbo.USP_AdminListTransactions
    @TxnType NVARCHAR(30) = NULL,
    @PlayerId UNIQUEIDENTIFIER = NULL,
    @FromDate DATE = NULL,
    @ToDate DATE = NULL,
    @PageNo INT = 1,
    @PageSize INT = 50
AS
BEGIN
    SET NOCOUNT ON;
    IF @PageNo < 1 SET @PageNo = 1;
    IF @PageSize < 1 SET @PageSize = 50;
    IF @PageSize > 200 SET @PageSize = 200;
    DECLARE @Offset INT = (@PageNo - 1) * @PageSize;

    SELECT
        T.TxnId,
        T.PlayerId,
        P.Username,
        P.ContactEmail,
        P.ContactPhone,
        T.TxnType,
        T.AmountPaise,
        T.BalanceAfter,
        T.Status,
        T.Gateway,
        T.GatewayRef,
        T.Remarks,
        T.CreatedAt
    FROM Transactions T
    INNER JOIN Players P ON P.PlayerId = T.PlayerId
    WHERE (@TxnType IS NULL OR T.TxnType = @TxnType)
      AND (@PlayerId IS NULL OR T.PlayerId = @PlayerId)
      AND (@FromDate IS NULL OR CAST(T.CreatedAt AS DATE) >= @FromDate)
      AND (@ToDate IS NULL OR CAST(T.CreatedAt AS DATE) <= @ToDate)
    ORDER BY T.CreatedAt DESC
    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
END
GO

CREATE OR ALTER PROCEDURE dbo.USP_AdminSearchPlayers
    @Query NVARCHAR(150) = NULL,
    @PageNo INT = 1,
    @PageSize INT = 50
AS
BEGIN
    SET NOCOUNT ON;
    IF @PageNo < 1 SET @PageNo = 1;
    IF @PageSize < 1 SET @PageSize = 50;
    IF @PageSize > 100 SET @PageSize = 100;
    DECLARE @Offset INT = (@PageNo - 1) * @PageSize;
    DECLARE @Q NVARCHAR(152) = NULL;
    IF @Query IS NOT NULL AND LTRIM(RTRIM(@Query)) <> N''
        SET @Q = N'%' + LTRIM(RTRIM(@Query)) + N'%';

    SELECT
        P.PlayerId,
        P.Username,
        P.ContactEmail,
        P.ContactPhone,
        P.StateCode,
        P.IsActive,
        P.IsBanned,
        P.CreatedAt,
        P.LastLoginAt,
        W.BalancePaise,
        W.DepositPaise,
        W.WithdrawnPaise,
        K.IsFullyVerified
    FROM Players P
    LEFT JOIN Wallets W ON W.PlayerId = P.PlayerId
    LEFT JOIN PlayerKYC K ON K.PlayerId = P.PlayerId
    WHERE @Q IS NULL
       OR P.Username LIKE @Q
       OR P.ContactEmail LIKE @Q
       OR P.ContactPhone LIKE @Q
       OR CAST(P.PlayerId AS NVARCHAR(36)) LIKE @Q
    ORDER BY P.CreatedAt DESC
    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
END
GO

CREATE OR ALTER PROCEDURE dbo.USP_AdminGetPlayerDetail
    @PlayerId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        P.PlayerId,
        P.Username,
        P.ContactEmail,
        P.ContactPhone,
        P.StateCode,
        P.ReferralCode,
        P.IsActive,
        P.IsBanned,
        P.BannedReason,
        P.CreatedAt,
        P.LastLoginAt,
        W.BalancePaise,
        W.DepositPaise,
        W.WinningPaise,
        W.WithdrawnPaise,
        W.BonusPaise,
        S.TotalGames,
        S.TotalWins,
        S.TotalLosses,
        S.WinRatePct,
        K.AadhaarStatus,
        K.PANStatus,
        K.BankStatus,
        K.IsFullyVerified
    FROM Players P
    LEFT JOIN Wallets W ON W.PlayerId = P.PlayerId
    LEFT JOIN PlayerStats S ON S.PlayerId = P.PlayerId
    LEFT JOIN PlayerKYC K ON K.PlayerId = P.PlayerId
    WHERE P.PlayerId = @PlayerId;
END
GO

CREATE OR ALTER PROCEDURE dbo.USP_AdminPlayerFinanceDaily
    @PlayerId UNIQUEIDENTIFIER,
    @Days INT = 30
AS
BEGIN
    SET NOCOUNT ON;
    IF @Days < 1 SET @Days = 1;
    IF @Days > 365 SET @Days = 365;

    SELECT
        CAST(CreatedAt AS DATE) AS [Date],
        SUM(CASE WHEN TxnType = 'Deposit' AND Status = 'Success' THEN AmountPaise ELSE 0 END) AS DepositsPaise,
        SUM(CASE WHEN TxnType = 'Withdrawal' AND Status = 'Success' THEN AmountPaise ELSE 0 END) AS WithdrawalsPaise,
        SUM(CASE WHEN TxnType = 'EntryFee' AND Status = 'Success' THEN AmountPaise ELSE 0 END) AS EntryFeesPaise,
        SUM(CASE WHEN TxnType = 'Reward' AND Status = 'Success' THEN AmountPaise ELSE 0 END) AS RewardsPaise
    FROM Transactions
    WHERE PlayerId = @PlayerId
      AND CreatedAt >= DATEADD(DAY, -@Days, GETDATE())
    GROUP BY CAST(CreatedAt AS DATE)
    ORDER BY [Date];
END
GO

PRINT 'Updates 033 applied: Admin console stored procedures.';
GO
