USE OnwardsSwiftDB;
GO

/*
One-time backfill to hydrate missing SystemUsers profile fields from completed
signup wizard rows keyed by normalized National ID.
*/
IF OBJECT_ID('dbo.signup_wizard_profiles', 'U') IS NULL
BEGIN
    PRINT 'signup_wizard_profiles table not found. No backfill applied.';
    RETURN;
END;
GO

;WITH latest_profile AS
(
    SELECT
        UPPER(REPLACE(REPLACE(ISNULL(national_id, ''), ' ', ''), '-', '')) AS normalized_national_id,
        NULLIF(LTRIM(RTRIM(full_name)), '') AS full_name,
        NULLIF(LTRIM(RTRIM(email)), '') AS email,
        NULLIF(LTRIM(RTRIM(phone)), '') AS phone,
        NULLIF(LTRIM(RTRIM(kra_pin)), '') AS kra_pin,
        NULLIF(LTRIM(RTRIM(gender)), '') AS gender,
        NULLIF(LTRIM(RTRIM(postal_address)), '') AS postal_address,
        NULLIF(LTRIM(RTRIM(physical_address)), '') AS physical_address,
        NULLIF(LTRIM(RTRIM(iprs_reference)), '') AS iprs_reference,
        identity_verified,
        ROW_NUMBER() OVER
        (
            PARTITION BY UPPER(REPLACE(REPLACE(ISNULL(national_id, ''), ' ', ''), '-', ''))
            ORDER BY updated_at_utc DESC, created_at_utc DESC
        ) AS rn
    FROM dbo.signup_wizard_profiles
    WHERE ISNULL(account_created, 0) = 1
)
UPDATE su
SET
    FullName = COALESCE(NULLIF(LTRIM(RTRIM(su.FullName)), ''), lp.full_name),
    Email = COALESCE(NULLIF(LTRIM(RTRIM(su.Email)), ''), lp.email),
    Phone = COALESCE(NULLIF(LTRIM(RTRIM(su.Phone)), ''), lp.phone),
    KraPin = COALESCE(NULLIF(LTRIM(RTRIM(su.KraPin)), ''), lp.kra_pin),
    Gender = COALESCE(NULLIF(LTRIM(RTRIM(su.Gender)), ''), lp.gender),
    PostalAddress = COALESCE(NULLIF(LTRIM(RTRIM(su.PostalAddress)), ''), lp.postal_address),
    PhysicalAddress = COALESCE(NULLIF(LTRIM(RTRIM(su.PhysicalAddress)), ''), lp.physical_address),
    IprsReference = COALESCE(NULLIF(LTRIM(RTRIM(su.IprsReference)), ''), lp.iprs_reference),
    IprsVerified = CASE WHEN su.IprsVerified = 1 THEN 1 ELSE ISNULL(lp.identity_verified, 0) END,
    UpdatedAt = SYSUTCDATETIME()
FROM dbo.SystemUsers su
INNER JOIN latest_profile lp
    ON UPPER(REPLACE(REPLACE(ISNULL(su.NationalId, ''), ' ', ''), '-', '')) = lp.normalized_national_id
WHERE lp.rn = 1
  AND ISNULL(su.IsDeleted, 0) = 0
  AND (
      NULLIF(LTRIM(RTRIM(su.FullName)), '') IS NULL OR
      NULLIF(LTRIM(RTRIM(su.Email)), '') IS NULL OR
      NULLIF(LTRIM(RTRIM(su.Phone)), '') IS NULL OR
      NULLIF(LTRIM(RTRIM(su.KraPin)), '') IS NULL OR
      NULLIF(LTRIM(RTRIM(su.Gender)), '') IS NULL OR
      NULLIF(LTRIM(RTRIM(su.PostalAddress)), '') IS NULL OR
      NULLIF(LTRIM(RTRIM(su.PhysicalAddress)), '') IS NULL OR
      NULLIF(LTRIM(RTRIM(su.IprsReference)), '') IS NULL OR
      ISNULL(su.IprsVerified, 0) = 0
  );
GO
