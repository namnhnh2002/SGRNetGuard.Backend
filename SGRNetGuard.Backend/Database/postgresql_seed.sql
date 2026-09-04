-- Safe base defaults. Add the full site list from 02_seed_sites.sql as needed.
INSERT INTO public.Sites (Region, SiteName, Subnet, ItAccount, TeamsAccount)
VALUES ('VMB', 'Sun City Hà Nội', '10.29.0.0/16', 'HUNGVD', 'hungvd@sungroup.com.vn')
ON CONFLICT (Region, SiteName) DO UPDATE SET Subnet = EXCLUDED.Subnet, ItAccount = EXCLUDED.ItAccount, TeamsAccount = EXCLUDED.TeamsAccount, UpdatedAtUtc = CURRENT_TIMESTAMP;
INSERT INTO public.RegionDnsServers (Region, DnsServer) VALUES
    ('VMB', '192.168.131.63'), ('VMB', '192.168.131.64'), ('VMB', '10.129.4.101'), ('VMB', '10.129.4.102')
ON CONFLICT (Region, DnsServer) DO NOTHING;