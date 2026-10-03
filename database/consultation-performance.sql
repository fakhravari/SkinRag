IF OBJECT_ID(N'dbo.ConsultationPerformanceLogs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ConsultationPerformanceLogs
    (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ConsultationPerformanceLogs PRIMARY KEY,
        StartedAtUtc DATETIME2(3) NOT NULL,
        CompletedAtUtc DATETIME2(3) NOT NULL,
        TotalMs BIGINT NOT NULL,
        IntentMs FLOAT NULL,
        CatalogReadMs FLOAT NULL,
        QueryBuildMs FLOAT NULL,
        SqlFilterMs FLOAT NULL,
        EmbeddingMs FLOAT NULL,
        ProductLoadMs FLOAT NULL,
        AnswerGenerationMs FLOAT NULL,
        ValidationMs FLOAT NULL,
        Outcome NVARCHAR(20) NOT NULL,
        ErrorType NVARCHAR(200) NULL,
        Intent NVARCHAR(40) NULL,
        RetrievalMethod NVARCHAR(60) NULL,
        ResponseMode NVARCHAR(30) NULL,
        ProductCount INT NULL
    );

    CREATE INDEX IX_ConsultationPerformanceLogs_StartedAtUtc
        ON dbo.ConsultationPerformanceLogs (StartedAtUtc DESC);
END;

-- Recent requests and their per-stage durations, in milliseconds.
-- SELECT TOP (100) * FROM dbo.ConsultationPerformanceLogs ORDER BY StartedAtUtc DESC;
