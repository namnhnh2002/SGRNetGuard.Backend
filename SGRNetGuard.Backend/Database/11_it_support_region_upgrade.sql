DO $region_seed$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM public.AppSettings WHERE Key = 'ItSupportRegionSeedV1') THEN
        UPDATE public.ITSupport legacy
        SET IsActive = TRUE, UpdatedAt = CURRENT_TIMESTAMP
        WHERE legacy.Site IS NULL
          AND legacy.Region IN ('VMB', 'VMT', 'VMN')
          AND legacy.Id = (
              SELECT candidate.Id
              FROM public.ITSupport candidate
              WHERE candidate.Site IS NULL
                AND upper(btrim(candidate.Region)) = upper(btrim(legacy.Region))
              ORDER BY candidate.SortOrder, candidate.Id
              LIMIT 1
          )
          AND NOT EXISTS (
              SELECT 1
              FROM public.ITSupport active
              WHERE active.Site IS NULL
                AND active.IsActive = TRUE
                AND upper(btrim(active.Region)) = upper(btrim(legacy.Region))
          );

        INSERT INTO public.AppSettings (Key, Value)
        VALUES ('ItSupportRegionSeedV1', 'complete')
        ON CONFLICT (Key) DO NOTHING;
    END IF;
END
$region_seed$;

CREATE UNIQUE INDEX IF NOT EXISTS ux_itsupport_one_active_per_region
    ON public.ITSupport (upper(btrim(Region)))
    WHERE IsActive = TRUE AND Site IS NULL;