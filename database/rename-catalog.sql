-- Rename catalog display data using the user's approved brand list.
-- Prices, inventory, formulas, product/variant IDs and SKU references are preserved.
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

DECLARE @ProductCount int = (SELECT COUNT(*) FROM dbo.Products);
DECLARE @VariantCount int = (SELECT COUNT(*) FROM dbo.ProductVariants);
DECLARE @Brands TABLE (OldSlug nvarchar(80), NewSlug nvarchar(80), NewName nvarchar(100));
INSERT @Brands VALUES
('roshana-demo', 'bv', N'B&V'),
('nilava-demo', 'wb', N'W&B'),
('avisa-demo', 'ldora-care', N'L’dora Care'),
('herava-demo', 'ldora-herbal', N'L’DORA HERBAL'),
('larisa-demo', 'ldora-beauty', N'L’dora Beauty'),
('mehrava-demo', 'ellix', N'ELLIX'),
('sorina-demo', 'ellix-plus', N'ELLIX+'),
('venora-demo', 'pristive', N'PRISTIVE'),
('legacy-sample', 'panberes', N'پنبه ریز');

-- Capture previous display names in memory so all product references stay consistent.
SELECT b.Id, b.Name AS OldName, m.NewName, m.NewSlug
INTO #RenamedBrands
FROM dbo.Brands b JOIN @Brands m ON b.Slug IN (m.OldSlug, m.NewSlug);

IF (SELECT COUNT(*) FROM #RenamedBrands) <> 9
    THROW 50201, 'Expected all nine catalog brands. No changes committed.', 1;

UPDATE p SET Name = CASE
        WHEN CHARINDEX(b.OldName, p.Name) > 0 THEN REPLACE(p.Name, b.OldName, b.NewName)
        WHEN CHARINDEX(b.NewName, p.Name) = 0 THEN CONCAT(p.Name, N' ', b.NewName)
        ELSE p.Name END,
    Brand = b.NewName, UpdatedAtUtc = SYSUTCDATETIME()
FROM dbo.Products p JOIN #RenamedBrands b ON b.Id = p.BrandId;
DECLARE @RenamedProducts int = @@ROWCOUNT;

UPDATE b SET Name = m.NewName, Slug = m.NewSlug, Country = NULL, IsDemo = 0
FROM dbo.Brands b JOIN #RenamedBrands m ON m.Id = b.Id;

UPDATE dbo.Categories SET Name = CASE Slug
    WHEN 'skin' THEN N'مراقبت پوست'
    WHEN 'hair' THEN N'مراقبت مو'
    WHEN 'beauty' THEN N'آرایش و زیبایی'
    WHEN 'sensitive-cream' THEN N'کرم پوست حساس'
    WHEN 'tone-serum' THEN N'سرم روشن‌کننده صورت'
    WHEN 'body-wash' THEN N'شامپو بدن'
    WHEN 'heat-spray' THEN N'اسپری محافظ حرارت مو'
    WHEN 'face-powder' THEN N'پنکیک و پودر صورت'
    ELSE Name END;

UPDATE p SET Category = c.Name,
    Description = CASE WHEN p.Description LIKE N'%ساختگی%'
        OR p.Description LIKE N'%آزمایشی%' OR p.Description LIKE N'%تست%'
        THEN CONCAT(p.Name, N' از گروه ', c.Name, N'. روش مصرف و مشخصات نهایی را از روی برچسب محصول بررسی کنید.')
        ELSE p.Description END,
    Warnings = CASE WHEN p.Warnings LIKE N'%ساختگی%'
        OR p.Warnings LIKE N'%آزمایشی%' OR p.Warnings LIKE N'%تست%'
        THEN N'قبل از مصرف، روش استفاده و ترکیبات روی بسته‌بندی را بررسی کنید. در صورت بروز حساسیت، مصرف را متوقف کنید.'
        ELSE p.Warnings END,
    UpdatedAtUtc = SYSUTCDATETIME()
FROM dbo.Products p JOIN dbo.Categories c ON c.Id = p.CategoryId;

IF (SELECT COUNT(*) FROM dbo.Products) <> @ProductCount
    OR (SELECT COUNT(*) FROM dbo.ProductVariants) <> @VariantCount
    THROW 50202, 'Catalog row counts changed. No changes committed.', 1;
IF EXISTS (SELECT 1 FROM dbo.Products WHERE
    CONCAT(Name, Brand, Category, Description, Warnings) LIKE N'%آزمایشی%'
    OR CONCAT(Name, Brand, Category, Description, Warnings) LIKE N'%ساختگی%'
    OR CONCAT(Name, Brand, Category, Description, Warnings) LIKE N'%برند نمونه%'
    OR CONCAT(Name, Brand, Category, Description, Warnings) LIKE N'%تست%')
    THROW 50203, 'Test wording remains in catalog display data. No changes committed.', 1;

COMMIT TRANSACTION;
SELECT @RenamedProducts AS RenamedProducts, @ProductCount AS Products,
    @VariantCount AS Variants, (SELECT COUNT(*) FROM dbo.Brands) AS Brands,
    (SELECT COUNT(*) FROM dbo.Categories) AS Categories;
DROP TABLE #RenamedBrands;
