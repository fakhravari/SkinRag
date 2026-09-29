CREATE DATABASE SkinRagDb;
GO
USE SkinRagDb;
GO

CREATE TABLE dbo.Products
(
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Name NVARCHAR(200) NOT NULL,
    Brand NVARCHAR(100) NULL,
    Category NVARCHAR(100) NULL,
    SkinTypes NVARCHAR(300) NULL,
    Concerns NVARCHAR(500) NULL,
    Ingredients NVARCHAR(MAX) NULL,
    Description NVARCHAR(MAX) NULL,
    Warnings NVARCHAR(MAX) NULL,
    Price DECIMAL(18,2) NOT NULL,
    StockQuantity INT NOT NULL CONSTRAINT DF_Products_Stock DEFAULT 0,
    IsActive BIT NOT NULL CONSTRAINT DF_Products_Active DEFAULT 1
);
GO

INSERT INTO dbo.Products
(Name, Brand, Category, SkinTypes, Concerns, Ingredients, Description,
 Warnings, Price, StockQuantity, IsActive)
VALUES
(N'مرطوب‌کننده سبک صورت', N'برند نمونه', N'مرطوب‌کننده',
 N'چرب، مختلط', N'آبرسانی، حفظ رطوبت', N'گلیسیرین، سرامید',
 N'مرطوب‌کننده با بافت سبک جهت کمک به حفظ رطوبت پوست',
 N'در صورت بروز تحریک مصرف را متوقف کنید.', 1500000, 20, 1),
(N'شوینده ملایم صورت', N'برند نمونه', N'شوینده',
 N'چرب، مختلط، حساس', N'پاک‌سازی پوست', N'گلیسیرین',
 N'شوینده ملایم جهت پاک‌سازی روزانه پوست',
 N'از تماس با چشم خودداری شود.', 900000, 15, 1),
(N'کرم مرطوب‌کننده پوست خشک', N'برند نمونه', N'مرطوب‌کننده',
 N'خشک', N'خشکی، حفظ رطوبت', N'سرامید، هیالورونیک اسید',
 N'کرم مرطوب‌کننده جهت کمک به کاهش خشکی پوست',
 N'قبل از مصرف توضیحات روی محصول را مطالعه کنید.', 1800000, 10, 1);
GO
