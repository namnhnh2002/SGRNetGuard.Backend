ALTER TABLE public.ITSupport ADD COLUMN IF NOT EXISTS Site text;

DO $site_seed$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM public.AppSettings WHERE Key = 'ItSupportSiteSeedV2') THEN
        UPDATE public.ITSupport
        SET IsActive = FALSE, UpdatedAt = CURRENT_TIMESTAMP
        WHERE Site IS NULL AND IsActive = TRUE;

        INSERT INTO public.ITSupport
            (Site, Region, DisplayName, Username, Email, TeamsUrl, Phone, IsActive, SortOrder)
        SELECT
            site.SiteName,
            site.Region,
            site.ItAccount,
            site.ItAccount,
            CASE WHEN position('@' IN site.TeamsAccount) > 1 THEN site.TeamsAccount ELSE NULL END,
            NULL,
            NULL,
            TRUE,
            ROW_NUMBER() OVER (ORDER BY site.Region, site.SiteName)::integer
        FROM (
            SELECT DISTINCT ON (lower(btrim(candidate.SiteName)))
                candidate.SiteName,
                candidate.Region,
                candidate.ItAccount,
                candidate.TeamsAccount
            FROM public.Sites candidate
            WHERE candidate.IsActive = TRUE
              AND btrim(candidate.SiteName) <> ''
              AND btrim(candidate.ItAccount) <> ''
            ORDER BY lower(btrim(candidate.SiteName)), candidate.UpdatedAtUtc DESC, candidate.Id DESC
        ) site
        WHERE NOT EXISTS (
            SELECT 1
            FROM public.ITSupport existing
            WHERE lower(btrim(existing.Site)) = lower(btrim(site.SiteName))
        );

        INSERT INTO public.AppSettings (Key, Value)
        VALUES ('ItSupportSiteSeedV2', 'complete')
        ON CONFLICT (Key) DO NOTHING;
    END IF;
END
$site_seed$;

CREATE UNIQUE INDEX IF NOT EXISTS ux_itsupport_one_active_per_site
    ON public.ITSupport (lower(btrim(Site)))
    WHERE IsActive = TRUE AND Site IS NOT NULL;