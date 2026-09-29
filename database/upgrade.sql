-- Additive, repeatable upgrade: preserves all existing Products and IDs.
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.Products','U') IS NULL
    THROW 50001, 'Run database/sample.sql on a new database before this upgrade.', 1;

IF OBJECT_ID('dbo.Categories','U') IS NULL
CREATE TABLE dbo.Categories (
    Id INT IDENTITY PRIMARY KEY, Slug NVARCHAR(80) NOT NULL UNIQUE,
    Name NVARCHAR(100) NOT NULL, Domain NVARCHAR(20) NOT NULL,
    ParentId INT NULL REFERENCES dbo.Categories(Id),
    CONSTRAINT CK_Categories_Domain CHECK (Domain IN ('skin','hair','beauty'))
);
IF OBJECT_ID('dbo.Brands','U') IS NULL
CREATE TABLE dbo.Brands (
    Id INT IDENTITY PRIMARY KEY, Slug NVARCHAR(80) NOT NULL UNIQUE,
    Name NVARCHAR(100) NOT NULL, Country NVARCHAR(100) NULL,
    IsDemo BIT NOT NULL DEFAULT 0
);
IF OBJECT_ID('dbo.Profiles','U') IS NULL
CREATE TABLE dbo.Profiles (
    Id INT IDENTITY PRIMARY KEY, Slug NVARCHAR(80) NOT NULL UNIQUE,
    Name NVARCHAR(100) NOT NULL, Kind NVARCHAR(20) NOT NULL,
    CONSTRAINT CK_Profiles_Kind CHECK (Kind IN ('skin','hair'))
);
IF OBJECT_ID('dbo.Concerns','U') IS NULL
CREATE TABLE dbo.Concerns (
    Id INT IDENTITY PRIMARY KEY, Slug NVARCHAR(80) NOT NULL UNIQUE,
    Name NVARCHAR(100) NOT NULL, Domain NVARCHAR(20) NOT NULL,
    SearchTerms NVARCHAR(500) NOT NULL DEFAULT '',
    CONSTRAINT CK_Concerns_Domain CHECK (Domain IN ('skin','hair','beauty'))
);
IF OBJECT_ID('dbo.Ingredients','U') IS NULL
CREATE TABLE dbo.Ingredients (
    Id INT IDENTITY PRIMARY KEY, Slug NVARCHAR(80) NOT NULL UNIQUE,
    Name NVARCHAR(100) NOT NULL, InciName NVARCHAR(200) NOT NULL
);
IF COL_LENGTH('dbo.Products','Sku') IS NULL ALTER TABLE dbo.Products ADD Sku NVARCHAR(80) NULL;
IF COL_LENGTH('dbo.Products',
    'CategoryId') IS NULL ALTER TABLE dbo.Products ADD CategoryId INT NULL REFERENCES dbo.Categories(Id);
IF COL_LENGTH('dbo.Products',
    'BrandId') IS NULL ALTER TABLE dbo.Products ADD BrandId INT NULL REFERENCES dbo.Brands(Id);
IF COL_LENGTH('dbo.Products',
    'HairTypes') IS NULL ALTER TABLE dbo.Products ADD HairTypes NVARCHAR(300) NULL;
IF COL_LENGTH('dbo.Products',
    'UsageInstructions') IS NULL ALTER TABLE dbo.Products ADD UsageInstructions NVARCHAR(2000) NULL;
IF COL_LENGTH('dbo.Products',
    'SearchKeywords') IS NULL ALTER TABLE dbo.Products ADD SearchKeywords NVARCHAR(1000) NULL;
IF COL_LENGTH('dbo.Products',
    'IsDemo') IS NULL ALTER TABLE dbo.Products ADD IsDemo BIT NOT NULL CONSTRAINT DF_Products_Demo DEFAULT 0;
IF COL_LENGTH('dbo.Products',
    'FragranceFree') IS NULL ALTER TABLE dbo.Products ADD FragranceFree BIT NULL;
IF COL_LENGTH('dbo.Products',
    'Currency') IS NULL ALTER TABLE dbo.Products ADD Currency NVARCHAR(3) NOT NULL CONSTRAINT DF_Products_Currency DEFAULT 'IRR';
IF COL_LENGTH('dbo.Products',
    'UpdatedAtUtc') IS NULL ALTER TABLE dbo.Products ADD UpdatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_Products_Updated DEFAULT SYSUTCDATETIME();
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.Products') AND name='UX_Products_Sku')
CREATE UNIQUE INDEX UX_Products_Sku ON dbo.Products(Sku) WHERE Sku IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.Products') AND name='IX_Products_Catalog')
CREATE INDEX IX_Products_Catalog ON dbo.Products(CategoryId,
    BrandId,
    IsActive) INCLUDE (Price,
    StockQuantity);
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name='CK_Products_PriceStock')
ALTER TABLE dbo.Products ADD CONSTRAINT CK_Products_PriceStock CHECK (Price>=0 AND StockQuantity>=0);

IF OBJECT_ID('dbo.ProductProfiles','U') IS NULL
CREATE TABLE dbo.ProductProfiles (
    ProductId INT NOT NULL REFERENCES dbo.Products(Id),
    ProfileId INT NOT NULL REFERENCES dbo.Profiles(Id), PRIMARY KEY (ProductId,ProfileId)
);
IF OBJECT_ID('dbo.ProductConcerns','U') IS NULL
CREATE TABLE dbo.ProductConcerns (
    ProductId INT NOT NULL REFERENCES dbo.Products(Id),
    ConcernId INT NOT NULL REFERENCES dbo.Concerns(Id), PRIMARY KEY (ProductId,ConcernId)
);
IF OBJECT_ID('dbo.ProductIngredients','U') IS NULL
CREATE TABLE dbo.ProductIngredients (
    ProductId INT NOT NULL REFERENCES dbo.Products(Id),
    IngredientId INT NOT NULL REFERENCES dbo.Ingredients(Id), PRIMARY KEY (ProductId,IngredientId)
);
IF OBJECT_ID('dbo.ProductVariants','U') IS NULL
CREATE TABLE dbo.ProductVariants (
    Id INT IDENTITY PRIMARY KEY, ProductId INT NOT NULL REFERENCES dbo.Products(Id),
    Sku NVARCHAR(100) NOT NULL UNIQUE, Name NVARCHAR(200) NOT NULL,
    SizeValue DECIMAL(10,2) NOT NULL, SizeUnit NVARCHAR(10) NOT NULL,
    Shade NVARCHAR(100) NULL, Finish NVARCHAR(100) NULL,
    Price DECIMAL(18,2) NOT NULL, StockQuantity INT NOT NULL DEFAULT 0,
    IsActive BIT NOT NULL DEFAULT 1,
    CONSTRAINT CK_Variants_Values CHECK (Price>=0 AND StockQuantity>=0 AND SizeValue>0),
    CONSTRAINT CK_Variants_Unit CHECK (SizeUnit IN ('ml','g','pcs'))
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.ProductVariants') AND name='IX_Variants_Availability')
CREATE INDEX IX_Variants_Availability ON dbo.ProductVariants(ProductId,
    IsActive,
    Price) INCLUDE (StockQuantity);
IF OBJECT_ID('dbo.ProductEmbeddings','U') IS NULL
CREATE TABLE dbo.ProductEmbeddings (
    ProductId INT NOT NULL REFERENCES dbo.Products(Id),
    Model NVARCHAR(100) NOT NULL, ContentHash NVARCHAR(64) NOT NULL,
    Dimensions INT NOT NULL, VectorJson NVARCHAR(MAX) NOT NULL,
    UpdatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    PRIMARY KEY (ProductId,Model),
    CONSTRAINT CK_Embedding_Vector CHECK (ISJSON(VectorJson)=1 AND Dimensions>0)
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.ProductProfiles') AND name='IX_ProductProfiles_Profile')
CREATE INDEX IX_ProductProfiles_Profile ON dbo.ProductProfiles(ProfileId,ProductId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.ProductConcerns') AND name='IX_ProductConcerns_Concern')
CREATE INDEX IX_ProductConcerns_Concern ON dbo.ProductConcerns(ConcernId,ProductId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.ProductIngredients') AND name='IX_ProductIngredients_Ingredient')
CREATE INDEX IX_ProductIngredients_Ingredient ON dbo.ProductIngredients(IngredientId,ProductId);
COMMIT TRANSACTION;
GO
