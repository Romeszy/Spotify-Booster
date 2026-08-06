-- Pin the public health RPC owner explicitly. This remains separate from the
-- original migration so already-applied migration history is never rewritten.
alter function public.spotify_booster_health_v1() owner to postgres;
