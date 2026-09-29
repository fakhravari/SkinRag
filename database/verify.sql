SET NOCOUNT ON;
IF (SELECT COUNT(*) FROM dbo.Products WHERE Sku LIKE 'DEMO-%') <> 360
    THROW 50101, 'Expected exactly 360 generated demo products.', 1;
IF (SELECT COUNT(*) FROM dbo.ProductVariants WHERE Sku LIKE 'DEMO-%') <> 1080
    THROW 50102, 'Expected exactly 1080 generated demo variants.', 1;
IF EXISTS (SELECT Sku FROM dbo.Products WHERE Sku IS NOT NULL GROUP BY Sku HAVING COUNT(*)>1)
    THROW 50103, 'Duplicate product SKU.', 1;
IF EXISTS (SELECT 1 FROM dbo.Products WHERE Sku LIKE 'DEMO-%' AND (CategoryId IS NULL OR BrandId IS NULL OR FragranceFree IS NULL))
    THROW 50104, 'Demo product missing normalized metadata.', 1;
IF EXISTS (SELECT 1 FROM dbo.Products p WHERE p.Sku LIKE 'DEMO-%'
    AND (NOT EXISTS (SELECT 1 FROM dbo.ProductProfiles x WHERE x.ProductId=p.Id)
      OR NOT EXISTS (SELECT 1 FROM dbo.ProductConcerns x WHERE x.ProductId=p.Id)
      OR NOT EXISTS (SELECT 1 FROM dbo.ProductIngredients x WHERE x.ProductId=p.Id)))
    THROW 50105, 'Demo product missing profile/concern/ingredient associations.', 1;
IF (SELECT COUNT(*) FROM dbo.Products WHERE Id IN(1,2,3) AND
    ((Id=1 AND Name=N'مرطوب‌کننده سبک صورت' AND Price=1500000 AND StockQuantity=20)
    OR (Id=2 AND Name=N'شوینده ملایم صورت' AND Price=900000 AND StockQuantity=15)
    OR (Id=3 AND Name=N'کرم مرطوب‌کننده پوست خشک' AND Price=1800000 AND StockQuantity=10))) <> 3
    THROW 50106, 'Original sample products were changed.', 1;
DBCC CHECKCONSTRAINTS WITH ALL_CONSTRAINTS;
SELECT c.Domain,COUNT(*) AS Products FROM dbo.Products p JOIN dbo.Categories c ON c.Id=p.CategoryId GROUP BY c.Domain;
SELECT COUNT(*) AS Variants,SUM(CASE WHEN StockQuantity=0 THEN 1 ELSE 0 END) AS SoldOutVariants,
    SUM(CASE WHEN IsActive=0 THEN 1 ELSE 0 END) AS InactiveVariants FROM dbo.ProductVariants;
PRINT 'Demo catalog verification passed.';
