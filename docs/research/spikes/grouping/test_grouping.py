#!/usr/bin/env python3
"""Unit tests for the grouping/newness design (python3 -m unittest -v test_grouping)."""
import os, sys, unittest
from datetime import date, datetime, timedelta, timezone
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import grouping as G

UTC = timezone.utc
H4 = timedelta(hours=4)
ANVIL = (64.5627, -165.3696); COUNCIL = (64.6935, -164.2657)
NOME_A = (64.6932, -165.7665); NOME_B = (64.5925, -165.6731)          # 12.0 km apart, same folder
ZACHAR = (57.5368, -153.7484); KODIAK_TOWN = (57.7996, -152.3902)     # 85 km apart
NEWPORT_AM = (41.5155, -71.2967); NEWPORT_PM = (41.4762, -71.3237)    # 4.8 km apart
MAKAHA = (21.4700, -158.2100)
PC = 'America/Anchorage'


def vid(dc, idx, loc=None, size=None, moov=True, session=None, drone_minus_utc=-4):
    """DJI clip; dc = drone-clock filename stamp 'YYYYMMDDHHMMSS'."""
    t = datetime.strptime(dc, '%Y%m%d%H%M%S')
    utc = (t - timedelta(hours=drone_minus_utc)).replace(tzinfo=UTC)
    n = f'DJI_{dc}_{idx:04d}_D.MP4'
    return G.RawItem(n, 'video', n, size or 100_000_000 + idx, 'dji', utc + timedelta(seconds=90),
                     mvhd_utc=utc if moov else None, lat=loc and loc[0], lon=loc and loc[1],
                     session_utc=session)


def dng(dc, idx, loc=None, size=None):
    t = datetime.strptime(dc, '%Y%m%d%H%M%S')
    n = f'DJI_{dc}_{idx:04d}_D.DNG'
    return G.RawItem(n, 'photo', n, size or 25_000_000 + idx, 'dji', (t + H4).replace(tzinfo=UTC),
                     exif_dto=t, lat=loc and loc[0], lon=loc and loc[1])


def lib_of(raws, folder_rel, root='video'):
    return [G.LibFile(f'{folder_rel}/{r.name}', r.size, r.mtime_utc, root) for r in raws]


def groups(raws, p=G.Params(), **kw):
    items = G.normalize(raws, p, PC, **kw)
    return G.cluster([i for i in items if i.kind == 'video'], p), items


def ids(gs):
    return [[i.id[4:18] for i in g.items] for g in gs]


class Normalisation(unittest.TestCase):
    def test_midnight_local_date_uses_site_zone_not_filename(self):
        # drone clock 7/26 03:50 = 07:50Z = 7/25 23:50 AKDT ; 04:10 -> 7/26 00:10 AKDT
        gs, items = groups([vid('20260726035000', 1, ANVIL), vid('20260726041000', 2, ANVIL)])
        self.assertEqual(len(gs), 1)
        self.assertEqual(gs[0].start, date(2026, 7, 25))
        self.assertEqual(G.proposed_rel(gs[0].start, 'Anvil'), '2026/2026-07/2026-07-25 Anvil')

    def test_midnight_with_G0_not_split_inside_session(self):
        gs, _ = groups([vid('20260726035000', 1, ANVIL), vid('20260726041000', 2, ANVIL)],
                       G.Params(gap_days=0))
        self.assertEqual(len(gs), 1)      # H = 3 h guard

    def test_dng_uses_learned_offset_and_site_zone(self):
        raws = [vid('20260726035000', 1, ANVIL), dng('20260726035500', 2, ANVIL)]
        items = {i.id: i for i in G.normalize(raws, G.Params(), PC)}
        d = items['DJI_20260726035500_0002_D.DNG']
        self.assertEqual(d.time_source, 'droneclock+learned')
        self.assertEqual(d.utc, datetime(2026, 7, 26, 7, 55, tzinfo=UTC))
        self.assertEqual(d.local_date, date(2026, 7, 25))

    def test_hawaii_site_zone_differs_from_pc_zone(self):
        # 09:30Z = 23:30 HST on 2/28, but 00:30 AKST on 3/1
        items = G.normalize([vid('20260301053000', 1, MAKAHA)], G.Params(), PC)
        self.assertEqual(items[0].tz, 'Pacific/Honolulu')
        self.assertEqual(items[0].local_date, date(2026, 2, 28))

    def test_offset_nearest_sample_handles_clock_change(self):
        raws = [vid('20261020120000', 1, ANVIL, drone_minus_utc=-4),
                vid('20261110120000', 2, ANVIL, drone_minus_utc=-5),
                dng('20261110121000', 3, ANVIL)]
        d = [i for i in G.normalize(raws, G.Params(), PC) if i.kind == 'photo'][0]
        self.assertEqual(d.utc, datetime(2026, 11, 10, 17, 10, tzinfo=UTC))

    def test_offset_from_setting_when_card_has_no_mp4(self):
        d = G.normalize([dng('20260927150000', 1, ZACHAR)], G.Params(), PC, clock_setting=-H4)[0]
        self.assertEqual((d.time_source, d.utc), ('droneclock+setting', datetime(2026, 9, 27, 19, 0, tzinfo=UTC)))
        d = G.normalize([dng('20260927150000', 1, ZACHAR)], G.Params(), PC)[0]
        self.assertEqual(d.time_source, 'mtime')

    def test_truncated_clip_without_moov_uses_filename_and_joins(self):
        gs, items = groups([vid('20260726235645', 1, ANVIL), vid('20260727002013', 14, ANVIL, moov=False),
                            vid('20260727002118', 15, ANVIL)])
        t = [i for i in items if i.id.endswith('0014_D.MP4')][0]
        self.assertEqual(t.time_source, 'droneclock+learned')
        self.assertEqual(t.utc, datetime(2026, 7, 27, 4, 20, 13, tzinfo=UTC))
        self.assertEqual(len(gs), 1)

    def test_autel_floating_time_no_gps(self):
        raws = [G.RawItem(f'MAX_00{n}.MP4', 'video', f'MAX_00{n}.MP4', 1_000_000 * n, 'autel',
                          datetime(2022, 3, 27, 15, n - 60, tzinfo=UTC),
                          mvhd_utc=datetime(2022, 3, 27, 7, n - 60, tzinfo=UTC)) for n in (61, 62, 64, 65)]
        raws.append(G.RawItem('MAX_0063.DNG', 'photo', 'MAX_0063.DNG', 24_130_633, 'autel',
                              datetime(2022, 3, 27, 15, 6, 40, tzinfo=UTC)))
        items = G.normalize(raws, G.Params(), PC)
        self.assertEqual({i.local_date for i in items}, {date(2022, 3, 27)})
        self.assertEqual({i.time_source for i in items}, {'autel-mvhd-floating', 'mtime-floating'})
        self.assertEqual(len(G.cluster([i for i in items if i.kind == 'video'], G.Params())), 1)


class Clustering(unittest.TestCase):
    def test_trip_across_month_boundary(self):
        gs, _ = groups([vid('20260731230000', 1, ANVIL), vid('20260801230000', 2, (64.58, -165.40)),
                        vid('20260802230000', 3, ANVIL)])
        self.assertEqual(len(gs), 1)
        self.assertEqual(G.proposed_rel(gs[0].start, 'Trip'), '2026/2026-07/2026-07-31 Trip')

    def test_trip_across_year_boundary(self):
        gs, _ = groups([vid('20261231230000', 1, ANVIL), vid('20270101230000', 2, ANVIL)])
        self.assertEqual(G.proposed_rel(gs[0].start, 'NY'), '2026/2026-12/2026-12-31 NY')

    def test_two_day_gap_splits(self):
        gs, _ = groups([vid('20260722230000', 1, ANVIL), vid('20260725230000', 2, ANVIL)])
        self.assertEqual(len(gs), 2)

    def test_consecutive_days_far_apart_split(self):   # Council Road 7/25 vs Anvil 7/26, 53 km
        gs, _ = groups([vid('20260725232655', 117, COUNCIL), vid('20260726022937', 118, COUNCIL),
                        vid('20260726235645', 1, ANVIL), vid('20260727000012', 2, ANVIL)])
        self.assertEqual(ids(gs), [['20260725232655', '20260726022937'], ['20260726235645', '20260727000012']])

    def test_consecutive_days_same_place_join(self):   # Kodiak-style multi-day trip
        gs, _ = groups([vid('20260523015251', 40, KODIAK_TOWN), vid('20260523201928', 52, KODIAK_TOWN),
                        vid('20260524190521', 64, (57.75, -152.50)), vid('20260525092718', 85, KODIAK_TOWN)])
        self.assertEqual(len(gs), 1)
        self.assertEqual((gs[0].start, gs[0].end), (date(2026, 5, 22), date(2026, 5, 25)))

    def test_same_day_two_distant_sites(self):
        gs, _ = groups([vid('20260927140127', 123, ZACHAR), vid('20260927140144', 124, ZACHAR),
                        vid('20260927190000', 150, KODIAK_TOWN)])
        self.assertEqual(len(gs), 2)
        self.assertEqual([g.start for g in gs], [date(2026, 9, 27)] * 2)

    def test_same_day_A_B_A_is_not_re_merged(self):
        gs, _ = groups([vid('20260927140127', 1, ZACHAR), vid('20260927190000', 2, KODIAK_TOWN),
                        vid('20260927230000', 3, ZACHAR)])
        self.assertEqual(len(gs), 3)                      # user merges in UI (documented behaviour)

    def test_same_day_near_sites_join(self):
        gs, _ = groups([vid('20260510104103', 2, NEWPORT_AM), vid('20260510193745', 30, NEWPORT_PM)])
        self.assertEqual(len(gs), 1)
        gs, _ = groups([vid('20260704010948', 101, NOME_A), vid('20260704013615', 102, NOME_B)])
        self.assertEqual(len(gs), 1)

    def test_no_gps_clip_goes_to_nearer_neighbour(self):
        nogps = vid('20260726234000', 99, None)            # 7/26 19:40 AKDT, Anvil day
        gs, _ = groups([vid('20260725232655', 117, COUNCIL), nogps, vid('20260726235645', 1, ANVIL)])
        self.assertEqual(ids(gs), [['20260725232655'], ['20260726234000', '20260726235645']])

    def test_no_gps_clip_session_beats_time(self):
        s = datetime(2026, 7, 26, 3, 20, tzinfo=UTC)
        nogps = vid('20260726234000', 99, None, session=s)
        gs, _ = groups([vid('20260725232655', 117, COUNCIL, session=s), nogps, vid('20260726235645', 1, ANVIL)])
        self.assertEqual(ids(gs), [['20260725232655', '20260726234000'], ['20260726235645']])

    def test_no_gps_first_clip_of_new_session_follows_session(self):
        # site A 10:00 AKDT; drive 85 km; power on at B, first clip has no fix (10:20), next at 11:00
        s1, s2 = datetime(2026, 9, 27, 17, 55, tzinfo=UTC), datetime(2026, 9, 27, 18, 18, tzinfo=UTC)
        gs, _ = groups([vid('20260927140000', 1, ZACHAR, session=s1), vid('20260927142000', 2, None, session=s2),
                        vid('20260927150000', 3, KODIAK_TOWN, session=s2)])
        self.assertEqual(ids(gs), [['20260927140000'], ['20260927142000', '20260927150000']])

    def test_all_no_gps_time_only(self):
        gs, _ = groups([vid('20260725232655', 1), vid('20260726235645', 2), vid('20260729120000', 3)])
        self.assertEqual(len(gs), 2)

    def test_R_and_G_are_parameters(self):
        raws = [vid('20260725232655', 117, COUNCIL), vid('20260726235645', 1, ANVIL)]
        self.assertEqual(len(groups(raws, G.Params(radius_km=60))[0]), 1)
        self.assertEqual(len(groups(raws, G.Params(radius_km=25))[0]), 2)


class Decisions(unittest.TestCase):
    Z = [vid('20260927140127', 123, ZACHAR), vid('20260927140144', 124, ZACHAR), vid('20260927142416', 148, ZACHAR)]
    ZREL = '2026/2026-09/2026-09-27 Zachar Bay'

    def test_video_only_card_new_folder(self):
        vg, pg, _, _ = G.run(self.Z, [], 'Picture Offload/')
        self.assertEqual([(g.action, g.target) for g in vg], [('NewFolder', '2026/2026-09/2026-09-27')])
        self.assertEqual(pg, [])

    def test_append_today_via_card_leftovers(self):
        new = [vid('20260927160000', 160, ZACHAR), vid('20260927161000', 161, ZACHAR)]
        vg, _, _, _ = G.run(self.Z + new, lib_of(self.Z, self.ZREL), 'Picture Offload/')
        self.assertEqual((vg[0].action, vg[0].target), ('Append', self.ZREL))
        self.assertTrue(vg[0].confidence.startswith('high'))

    def test_append_today_via_ledger_centroid(self):
        led = [G.LedgerRec(r.name, r.size, r.mvhd_utc, 'video', self.ZREL, *ZACHAR, 'America/Anchorage') for r in self.Z]
        new = [vid('20260927160000', 160, ZACHAR)]
        vg, _, _, lib = G.run(new, lib_of(self.Z, self.ZREL), 'Picture Offload/', ledger=led)
        self.assertEqual((vg[0].action, vg[0].target, vg[0].confidence), ('Append', self.ZREL, 'high (date + location)'))

    def test_append_today_location_unknown_same_date_is_medium(self):
        new = [vid('20260927160000', 160, ZACHAR)]
        vg, _, _, _ = G.run(new, lib_of(self.Z, self.ZREL), 'Picture Offload/')
        self.assertEqual((vg[0].action, vg[0].target), ('Append', self.ZREL))
        self.assertTrue(vg[0].confidence.startswith('medium'))

    def test_next_day_far_away_new_folder(self):
        led = [G.LedgerRec(r.name, r.size, r.mvhd_utc, 'video', self.ZREL, *ZACHAR, 'America/Anchorage') for r in self.Z]
        new = [vid('20260928180000', 170, KODIAK_TOWN)]
        vg, _, _, _ = G.run(new, lib_of(self.Z, self.ZREL), 'Picture Offload/', ledger=led)
        self.assertEqual((vg[0].action, vg[0].target), ('NewFolder', '2026/2026-09/2026-09-28'))

    def test_next_day_same_place_appends(self):
        led = [G.LedgerRec(r.name, r.size, r.mvhd_utc, 'video', self.ZREL, *ZACHAR, 'America/Anchorage') for r in self.Z]
        vg, _, _, _ = G.run([vid('20260928180000', 170, ZACHAR)], lib_of(self.Z, self.ZREL), 'Picture Offload/', ledger=led)
        self.assertEqual((vg[0].action, vg[0].target), ('Append', self.ZREL))

    def test_next_day_location_unknown_defaults_new(self):
        vg, _, _, _ = G.run([vid('20260928180000', 170, ZACHAR)], lib_of(self.Z, self.ZREL), 'Picture Offload/')
        self.assertEqual(vg[0].action, 'NewFolder')

    def test_never_prepend_before_folder_name_date(self):
        vg, _, _, _ = G.run([vid('20260926180000', 170, ZACHAR)], lib_of(self.Z, self.ZREL), 'Picture Offload/')
        self.assertEqual(vg[0].action, 'NewFolder')

    def test_legacy_depth_autel_append(self):
        mk = lambda n: G.RawItem(f'MAX_00{n}.MP4', 'video', f'MAX_00{n}.MP4', 1_000_000 * n, 'autel',
                                 datetime(2022, 3, 27, 15, n - 60, tzinfo=UTC),
                                 mvhd_utc=datetime(2022, 3, 27, 7, n - 60, tzinfo=UTC))
        old = [mk(61), mk(62), mk(64)]
        vg, _, _, _ = G.run(old + [mk(65)], lib_of(old, '2022/2022-03-27 Makaha Valley'), 'Picture Offload/')
        self.assertEqual((vg[0].action, vg[0].target), ('Append', '2022/2022-03-27 Makaha Valley'))

    def test_append_split_follows_user_boundaries(self):
        d1 = [vid('20260801200000', 1, ANVIL), vid('20260801201000', 2, ANVIL)]
        d2 = [vid('20260802200000', 3, ANVIL), vid('20260802203000', 5, ANVIL)]
        lib = lib_of(d1, '2026/2026-08/2026-08-01 Anvil AM') + lib_of(d2, '2026/2026-08/2026-08-02 Anvil PM')
        new = [vid('20260801202000', 9, ANVIL), vid('20260802202000', 4, ANVIL)]
        vg, _, _, _ = G.run(d1 + d2 + new, lib, 'Picture Offload/')
        self.assertEqual(vg[0].action, 'AppendSplit')
        self.assertEqual(sorted(vg[0].target.values()), ['2026/2026-08/2026-08-01 Anvil AM', '2026/2026-08/2026-08-02 Anvil PM'])

    def test_leftovers_card(self):
        council = [vid('20260725232655', 117, COUNCIL), vid('20260726022937', 118, COUNCIL)]
        anvil = [vid('20260726235645', 1, ANVIL), vid('20260727000012', 2, ANVIL)]
        lib = lib_of(council, '2026/2026-07/2026-07-25 Council Road') + lib_of(anvil, '2026/2026-07/2026-07-26 Anvil Mountain')
        photos = [dng('20260725233000', 116, COUNCIL),      # 7/25, videos imported that day
                  dng('20260927140000', 122, ZACHAR),       # after watermark (+ new-video date)
                  dng('20260815200000', 119, ANVIL)]        # photo-only day after watermark
        vg, pg, items, _ = G.run(council + anvil + self.Z + photos, lib, 'Picture Offload/')
        self.assertEqual([g.action for g in vg], ['AlreadyImported', 'AlreadyImported', 'NewFolder'])
        st = {i.id[4:18]: i.status for i in items if i.kind == 'photo'}
        self.assertEqual(st, {'20260725233000': 'ProbablyImported', '20260927140000': 'New', '20260815200000': 'New'})
        self.assertEqual([g.action for g in pg], ['PhotosOnly'])        # the 8/15 photo
        self.assertEqual(len(vg[2].photos) + len(vg[0].photos), 2)

    def test_photo_only_day_before_watermark_is_probably_imported(self):
        lib = lib_of(self.Z, self.ZREL)
        vg, pg, items, _ = G.run([dng('20260815200000', 119, ANVIL)], lib, 'Picture Offload/')
        self.assertEqual(vg, [])
        self.assertEqual(items[0].status, 'ProbablyImported')
        self.assertIn('photo-only', items[0].reason)

    def test_photo_only_card_after_watermark(self):
        lib = lib_of(self.Z, self.ZREL)
        ph = [dng('20260930200000', 200, ZACHAR), dng('20260930200500', 201, ZACHAR)]
        vg, pg, items, _ = G.run(ph, lib, 'Picture Offload/', clock_setting=-H4)
        self.assertEqual({i.status for i in items}, {'New'})
        self.assertEqual([(g.action, len(g.items)) for g in pg], [('PhotosOnly', 2)])

    def test_photo_in_photo_root_is_imported_and_pano_sets_are_distinct(self):
        pano87 = G.RawItem('001_0087', 'set', '001_0087', 2, 'dji', datetime(2026, 5, 25, 13, 30, tzinfo=UTC),
                           exif_dto=datetime(2026, 5, 25, 9, 30, 28), lat=57.7996, lon=-152.3902,
                           members=(('PANO_0001.DNG', 13751808), ('PANO_0002.DNG', 12882432)))
        pano112 = G.RawItem('001_0112', 'set', '001_0112', 2, 'dji', datetime(2026, 5, 25, 14, 0, tzinfo=UTC),
                            exif_dto=datetime(2026, 5, 25, 10, 0, 0), lat=57.7996, lon=-152.3902,
                            members=(('PANO_0001.DNG', 13751808), ('PANO_0002.DNG', 12882432)))
        lib = [G.LibFile('Picture Offload/001_0087/PANO_0001.DNG', 13751808, datetime.now(UTC), 'photo'),
               G.LibFile('Picture Offload/001_0087/PANO_0002.DNG', 12882432, datetime.now(UTC), 'photo')]
        _, _, items, _ = G.run([pano87, pano112], lib, 'Picture Offload/', clock_setting=-H4)
        self.assertEqual({i.id: i.status for i in items}['001_0087'], 'Imported')
        self.assertNotEqual({i.id: i.status for i in items}['001_0112'], 'Imported')

    def test_video_conflict_duplicate_removed(self):
        a = vid('20260927140127', 123, ZACHAR, size=105764094)
        conflict = vid('20260927140127', 123, ZACHAR, size=7340032)             # truncated earlier copy
        renamed = G.LibFile(self.ZREL + '/best shot.MP4', 555_555_555, datetime.now(UTC))
        dup = vid('20260927150000', 130, ZACHAR, size=555_555_555)
        gone = vid('20260927151000', 131, ZACHAR)
        led = [G.LedgerRec(gone.name, gone.size, gone.mvhd_utc, 'video', self.ZREL, *ZACHAR)]
        _, _, items, _ = G.run([conflict, dup, gone], lib_of([a], self.ZREL) + [renamed], 'Picture Offload/', ledger=led)
        st = {i.id[-10:-6]: i.status for i in items}
        self.assertEqual(st, {'0123': 'Conflict', '0130': 'PossibleDuplicate', '0131': 'RemovedFromLibrary'})

    def test_sanitize_and_naming(self):
        self.assertEqual(G.sanitize_desc('Newport, RI'), 'Newport, RI')
        self.assertEqual(G.sanitize_desc(' A/B: "test"?  '), 'A B test')
        self.assertEqual(G.sanitize_desc('Nome Rd...'), 'Nome Rd')
        self.assertEqual(G.proposed_rel(date(2026, 9, 27), '  '), '2026/2026-09/2026-09-27')


if __name__ == '__main__':
    unittest.main(verbosity=2)
