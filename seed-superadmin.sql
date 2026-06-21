-- ============================================================
-- CloudNews Lite — Seed SuperAdmin
-- Run this ONCE after your first migration.
--
-- The password hash below is for:  updated Custom password
-- Generate a new hash using: https://bcrypt-generator.com
--   (use cost factor 11)
-- Then replace the hash below with your own.
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM Users WHERE Email = 'bhuvneshwar.sh@gmail.com')
BEGIN
    INSERT INTO Users (FullName, Email, PasswordHash, Role, CreatedAt)
    VALUES (
        'Super Admin',
        'bhuvneshwar.sh@gmail.com',
        -- BCrypt hash for "Admin@123" — CHANGE THIS before going live!
        '$2a$12$Lg53ZKq3j9Z7thsGyNTRLucvfRY6tKuub0nyse4t6rJWfAfIqIXgm',
        'SuperAdmin',
        GETUTCDATE()
    );
    PRINT 'SuperAdmin seeded successfully.';
END
ELSE
BEGIN
    PRINT 'SuperAdmin already exists, skipping.';
END
GO
