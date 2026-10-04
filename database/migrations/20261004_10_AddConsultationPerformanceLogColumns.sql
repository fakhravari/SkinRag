SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.ConsultationPerformanceLogs', N'U') IS NULL
        THROW 51010, 'dbo.ConsultationPerformanceLogs does not exist.', 1;

    IF COL_LENGTH(N'dbo.ConsultationPerformanceLogs', N'QuerySource') IS NULL
        ALTER TABLE dbo.ConsultationPerformanceLogs ADD QuerySource nvarchar(500) NULL;

    IF COL_LENGTH(N'dbo.ConsultationPerformanceLogs', N'ModelCallsJson') IS NULL
        ALTER TABLE dbo.ConsultationPerformanceLogs ADD ModelCallsJson nvarchar(max) NULL;

    IF COL_LENGTH(N'dbo.ConsultationPerformanceLogs', N'SearchQuery') IS NULL
        ALTER TABLE dbo.ConsultationPerformanceLogs ADD SearchQuery nvarchar(max) NULL;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
