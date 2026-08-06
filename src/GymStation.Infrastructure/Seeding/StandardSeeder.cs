using GymStation.Domain.Attendance;
using GymStation.Domain.Contact;
using GymStation.Domain.Events;
using GymStation.Domain.Money;
using GymStation.Domain.Notifications;
using GymStation.Domain.People;
using GymStation.Domain.Ranks;
using GymStation.Domain.Scheduling;
using GymStation.Domain.Tenancy;
using GymStation.Infrastructure.Ranks;
using GymStation.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Infrastructure.Seeding;

/// <summary>
/// Builds the STANDARD TEST tenant (round 4.5): 300 people across four disciplines,
/// every account archetype sign-in-able, pinned dues states (exactly 14 behind:
/// 7×1mo, 5×2mo, 2×3mo), all ranks of every ladder held, and every pathology the
/// warning surfaces exist for — seeded on purpose. Deliberately SEPARATE from the
/// frozen pitch DemoSeeder. Deterministic: each section owns its own fixed-seed
/// Random (the 42/7/11 draw-order lesson, institutionalized), so extending one
/// section never reshuffles another. Refuses an existing slug; the test stack
/// resets by dropping the database, never by re-running in place.
/// </summary>
public class StandardSeeder(GymStationDbContext db, TenantContext tenant)
{
    private static readonly string[] FirstNames =
        ["Ari", "Bea", "Cal", "Dev", "Eli", "Fay", "Gil", "Hal", "Ida", "Jun",
         "Kai", "Lex", "Mia", "Ned", "Ora", "Pia", "Quil", "Rex", "Sol", "Tia",
         "Uma", "Vic", "Wes", "Xen", "Yara", "Zed", "Ash", "Blair", "Cruz", "Drew",
         "Emery", "Flynn", "Gray", "Hollis", "Indie", "Jules", "Kit", "Lane", "Marlo", "Nico",
         "Oakley", "Perry", "Reese", "Sage", "Tate", "Vale", "Winter", "Arden", "Briar", "Cove",
         "Dune", "Ellis", "Fern", "Gale", "Haven", "Isla", "Jett", "Lark", "Moss", "Nova"];

    private static readonly string[] LastNames =
        ["Abbot", "Birch", "Calder", "Danes", "Eaton", "Farrow", "Gates", "Hobbs", "Irwin", "Jarvis",
         "Keats", "Lowell", "Marsh", "Nolan", "Odell", "Paxton", "Quimby", "Rhodes", "Slater", "Thorne",
         "Usher", "Vance", "Whitley", "Yates", "Zell", "Ames", "Bond", "Corbin", "Dalton", "Eads",
         "Fisk", "Grantham", "Hale2", "Ingram", "Joyner", "Kerr", "Locke", "Mercer", "North", "Otis",
         "Pryor", "Quill", "Ramsey", "Stroud", "Tilden", "Underhill", "Voss", "Wilder", "Yeats", "Zorn",
         "Ashby2", "Bellamy", "Croft", "Devlin", "Ferris", "Goode", "Hawthorne", "Iverson", "Justice", "Kane"];

    private readonly HashSet<string> _usedNames = [];
    private readonly HashSet<string> _usedHandles = [];

    private Gym _gym = null!;
    private DateOnly _today;
    private string _slug = "";

    private MembershipPlan _adultPlan = null!;
    private MembershipPlan _mtPlan = null!;
    private MembershipPlan _judoPlan = null!;
    private MembershipPlan _fitPlan = null!;
    private MembershipPlan _kidsBjjPlan = null!;
    private MembershipPlan _kidsJudoPlan = null!;
    private MembershipPlan _familyStandard = null!;
    private MembershipPlan _familyPerHead = null!;
    private MembershipPlan _compedPlan = null!;
    private MembershipPlan _archivedPlan = null!;

    private readonly Dictionary<string, ClassType> _types = [];
    private readonly List<ClassTemplate> _templates = [];
    private ClassTemplate _pausedTemplate = null!;

    // Discipline pools drive attendance draws; a person may sit in several.
    private readonly List<Person> _bjjAdults = [];
    private readonly List<Person> _mtAdults = [];
    private readonly List<Person> _judoAdults = [];
    private readonly List<Person> _fitAdults = [];
    private readonly List<Person> _kidsBjj = [];
    private readonly List<Person> _kidsJudo = [];

    private Person _owner = null!;
    private Person _coachBjjHead = null!;
    private Person _coachBjjSecond = null!;
    private Person _coachJudo = null!;
    private Person _coachFit = null!;
    private Person _coachMt = null!;
    private Person _behindOneMonth = null!;
    private Person _behindThreeMonths = null!;
    private Person _coveredDormant = null!;
    private Person _wardFinn = null!;
    private Guid _guardianFinnUserId;

    private readonly List<Person> _behind1 = [];
    private readonly List<Person> _behind2 = [];
    private readonly List<Person> _behind3 = [];
    private readonly List<Person> _archivedMembers = [];
    private readonly List<(Family Family, Person Primary, MembershipPlan Plan, int Adults, int Kids)> _billedFamilies = [];

    public async Task<Guid> SeedAsync(string slug, string name, CancellationToken ct = default)
    {
        if (await db.Gyms.AnyAsync(g => g.Slug == slug, ct))
        {
            throw new InvalidOperationException($"A gym with slug '{slug}' already exists — standard seeding refuses to touch it.");
        }

        _slug = slug;
        _today = DateOnly.FromDateTime(DateTime.UtcNow);

        await SeedGymAsync(name, ct);
        SeedPlans();
        SeedTypesAndTemplates();
        await db.SaveChangesAsync(ct);

        await SeedPeopleAndFamiliesAsync(ct);
        await SeedRanksAsync(ct);
        await db.SaveChangesAsync(ct);

        await SeedScheduleHistoryAsync(ct);
        await db.SaveChangesAsync(ct);

        SeedLedger();
        SeedComms();
        await db.SaveChangesAsync(ct);

        tenant.Clear();
        return _gym.Id;
    }

    private async Task SeedGymAsync(string name, CancellationToken ct)
    {
        _gym = new Gym { Id = Guid.NewGuid(), Name = name, Slug = _slug, TimeZoneId = "America/Chicago" };
        db.Gyms.Add(_gym);
        await db.SaveChangesAsync(ct);
        tenant.SetGym(_gym.Id);

        // Distinct accent on purpose — one glance says "this is the TEST stack".
        db.GymSettings.Add(new GymSettings
        {
            GymId = _gym.Id,
            AccentColorHex = "#3E8E9E",
            SubstitutionMode = SubstitutionMode.AdminGate,
            OpenTime = new TimeOnly(6, 0),
            CloseTime = new TimeOnly(22, 0),
            AboutText = $"**{name}** is the standard test tenant: four disciplines, three hundred people, and every edge case on purpose. Nothing here is real; everything here resets.",
            ProgramsIntro = "Four programs, one resettable room.",
        });

        foreach (var categoryName in new[] { "RENT", "INSURANCE", "SOFTWARE", "UTILITIES", "MARKETING" })
        {
            db.ExpenseCategories.Add(new ExpenseCategory { Id = Guid.NewGuid(), Name = categoryName });
        }
    }

    private void SeedPlans()
    {
        _adultPlan = Plan("Adult Unlimited", 85m);
        _mtPlan = Plan("Muay Thai Only", 70m);
        _judoPlan = Plan("Judo Only", 60m);
        _fitPlan = Plan("Fitness Only", 50m);
        _kidsBjjPlan = Plan("Kids BJJ", 65m);
        _kidsJudoPlan = Plan("Kids Judo", 60m);
        _compedPlan = Plan("Comped Staff", 0m);
        _archivedPlan = Plan("Legacy Unlimited", 75m);
        _archivedPlan.Archived = true; // the DORMANT-pointer pathology's target

        _familyStandard = Plan("Family Standard", 150m);
        _familyStandard.Scope = PlanScope.Family;
        _familyStandard.IncludedAdults = 2;
        _familyStandard.IncludedKids = 2;
        _familyStandard.ExtraAdultPrice = 30m;
        _familyStandard.ExtraKidPrice = 20m;

        _familyPerHead = Plan("Family Per-Head", 0m);
        _familyPerHead.Scope = PlanScope.Family;
        _familyPerHead.ExtraAdultPrice = 80m;
        _familyPerHead.ExtraKidPrice = 50m;

        MembershipPlan Plan(string planName, decimal price)
        {
            var plan = new MembershipPlan { Id = Guid.NewGuid(), Name = planName, Price = price };
            db.MembershipPlans.Add(plan);
            return plan;
        }
    }

    private void SeedTypesAndTemplates()
    {
        foreach (var (tagName, color) in new[]
        {
            ("gi", "#C9503B"), ("no-gi", "#3E8E9E"), ("fundamentals", "#C9A227"), ("open-mat", "#707886"),
            ("kids", "#3E8E5A"), ("muay-thai", "#4A6FA5"), ("judo", "#2456A6"), ("fitness", "#B0622F"),
        })
        {
            _types[tagName] = new ClassType { Id = Guid.NewGuid(), Name = tagName, ColorHex = color };
            db.ClassTypes.Add(_types[tagName]);
        }

        var start = _today.AddDays(-7 * 12); // ADR 0004: history begins where the seed begins

        // BJJ ×8
        Template("Fundamentals", DayOfWeek.Monday, new(6, 0), 60, "gi", "fundamentals");
        Template("Adv Gi", DayOfWeek.Monday, new(18, 0), 90, "gi");
        Template("No-Gi", DayOfWeek.Tuesday, new(18, 0), 90, "no-gi");
        Template("Fundamentals", DayOfWeek.Wednesday, new(18, 0), 60, "gi", "fundamentals");
        Template("Adv Gi", DayOfWeek.Thursday, new(18, 0), 90, "gi");
        Template("No-Gi Lunch", DayOfWeek.Friday, new(12, 0), 60, "no-gi");
        Template("Open Mat", DayOfWeek.Saturday, new(10, 0), 120, "open-mat");
        Template("Comp Prep", DayOfWeek.Saturday, new(12, 30), 90, "gi");
        // Muay Thai ×4
        Template("Muay Thai", DayOfWeek.Monday, new(19, 30), 60, "muay-thai");
        Template("Muay Thai", DayOfWeek.Wednesday, new(19, 30), 60, "muay-thai");
        Template("Muay Thai Clinch", DayOfWeek.Friday, new(18, 0), 60, "muay-thai");
        Template("Muay Thai Pads", DayOfWeek.Saturday, new(9, 0), 60, "muay-thai");
        // Judo ×3
        Template("Judo", DayOfWeek.Tuesday, new(19, 30), 60, "judo");
        Template("Judo", DayOfWeek.Thursday, new(19, 30), 60, "judo");
        Template("Judo Randori", DayOfWeek.Saturday, new(11, 0), 60, "judo");
        // Fitness ×4 (mornings)
        Template("Fitness", DayOfWeek.Monday, new(7, 0), 45, "fitness");
        Template("Fitness", DayOfWeek.Wednesday, new(7, 0), 45, "fitness");
        Template("Fitness", DayOfWeek.Friday, new(7, 0), 45, "fitness");
        Template("Fitness Circuits", DayOfWeek.Saturday, new(8, 0), 45, "fitness");
        // Kids BJJ ×3
        Template("Kids BJJ", DayOfWeek.Monday, new(17, 0), 45, "kids", "gi");
        Template("Kids BJJ", DayOfWeek.Wednesday, new(17, 0), 45, "kids", "gi");
        Template("Kids BJJ", DayOfWeek.Saturday, new(9, 0), 45, "kids", "gi");
        // Kids Judo ×2
        Template("Kids Judo", DayOfWeek.Tuesday, new(17, 0), 45, "kids", "judo");
        Template("Kids Judo", DayOfWeek.Thursday, new(17, 0), 45, "kids", "judo");

        // The paused pathology: history exists, template rests — restore brings it back.
        _pausedTemplate = Template("Yoga for Grapplers", DayOfWeek.Sunday, new(9, 0), 60, "fitness");
        _pausedTemplate.Active = false;

        ClassTemplate Template(string templateName, DayOfWeek day, TimeOnly at, int minutes, params string[] tagNames)
        {
            var template = new ClassTemplate
            {
                Id = Guid.NewGuid(),
                Name = templateName,
                Day = day,
                StartTime = at,
                DurationMinutes = minutes,
                StartDate = start,
                ClassTypes = [.. tagNames.Select(t => _types[t])],
            };
            db.ClassTemplates.Add(template);
            _templates.Add(template);
            return template;
        }
    }

    private async Task SeedPeopleAndFamiliesAsync(CancellationToken ct)
    {
        var random = new Random(101);

        // ---- named archetypes (docs/test-roster.md is the human index) ----
        _owner = Cast("Val", "Moreau", PersonRoles.Owner | PersonRoles.Admin, new DateOnly(1979, 2, 11), null, 2014);
        Cast("Quinn", "Barlow", PersonRoles.Admin, new DateOnly(1991, 6, 30), null, 2019);
        var renIto = Cast("Ren", "Ito", PersonRoles.Admin | PersonRoles.Member, new DateOnly(1988, 9, 2), _adultPlan.Id, 2017);
        _coachBjjHead = Cast("Mateus", "Rocha", PersonRoles.Instructor | PersonRoles.Member, new DateOnly(1983, 4, 19), _compedPlan.Id, 2014);
        _coachBjjSecond = Cast("Talia", "Nunes", PersonRoles.Instructor | PersonRoles.Member, new DateOnly(1992, 12, 5), _compedPlan.Id, 2018);
        _coachMt = Cast("Anong", "Sit", PersonRoles.Instructor, new DateOnly(1986, 7, 23), _compedPlan.Id, 2022);
        _coachJudo = Cast("Hana", "Yoshida", PersonRoles.Instructor | PersonRoles.Member, new DateOnly(1989, 1, 14), _compedPlan.Id, 2020);
        _coachFit = Cast("Dee", "Cross", PersonRoles.Instructor | PersonRoles.Member, new DateOnly(1995, 10, 8), _compedPlan.Id, 2023);
        Cast("Pat", "Winters", PersonRoles.Staff, new DateOnly(1999, 3, 27), null, 2024, hasLogin: false); // #87-clean desk

        _behindOneMonth = Cast("Iris", "Vale", PersonRoles.Member, new DateOnly(1994, 5, 21), _adultPlan.Id, 2022);
        _behindThreeMonths = Cast("Cole", "Draper", PersonRoles.Member, new DateOnly(1990, 11, 3), _adultPlan.Id, 2021);
        var nilsBerg = Cast("Nils", "Berg", PersonRoles.Member, new DateOnly(1987, 8, 16), _archivedPlan.Id, 2016); // DORMANT pointer

        _bjjAdults.AddRange([renIto, _coachBjjHead, _coachBjjSecond, _behindOneMonth, _behindThreeMonths, nilsBerg]);
        _judoAdults.Add(_coachJudo);
        _fitAdults.Add(_coachFit);

        // Red-belt cameos: the top of the adult ladder is held by someone.
        foreach (var (first, last) in new[] { ("Aldo", "Pinto"), ("Vera", "Lobo"), ("Iwao", "Sato") })
        {
            _bjjAdults.Add(Cast(first, last, PersonRoles.Member, new DateOnly(1948 + _bjjAdults.Count % 8, 6, 12), null, 2014));
        }

        // Visitors: quick-added walk-ins, one destined for SET PLAN conversion.
        foreach (var (first, last) in new[] { ("Sky", "Tanaka"), ("Jo", "Marsh") })
        {
            var visitor = Cast(first, last, PersonRoles.Member, new DateOnly(1997, 4, 9), null, _today.Year, hasLogin: false);
            visitor.Visitor = true;
        }

        // Archived members WITH history — history stays, cycles skip them.
        foreach (var (first, last) in new[] { ("Ruth", "Calder"), ("Sol", "Ambrose") })
        {
            var archived = Cast(first, last, PersonRoles.Member, new DateOnly(1985, 2, 2), _adultPlan.Id, 2015);
            archived.Archived = true;
            _archivedMembers.Add(archived);
        }

        // ---- families: every shape the matrix supports ----
        // F1 FELD — Family Standard at included size (2A+2K = base $150); the
        // training-parent shape (primary's User LINKED to a member Person) and
        // the covered-dormant pathology (Noa keeps a personal plan, covered).
        _coveredDormant = Cast("Noa", "Feld", PersonRoles.Member, new DateOnly(1993, 3, 3), _adultPlan.Id, 2020);
        var gusFeld = Cast("Gus", "Feld", PersonRoles.Member, new DateOnly(1991, 7, 7), null, 2020);
        var mila = Cast("Mila", "Feld", PersonRoles.Member, _today.AddYears(-9), null, 2023, hasLogin: false);
        var ezra = Cast("Ezra", "Feld", PersonRoles.Member, _today.AddYears(-7), null, 2024, hasLogin: false);
        _bjjAdults.AddRange([_coveredDormant, gusFeld]);
        _kidsBjj.AddRange([mila, ezra]);
        var feld = Family("FELD FAMILY", gusFeld.UserId!.Value, _familyStandard.Id,
            adults: [_coveredDormant, gusFeld], wards: [mila, ezra]);
        _billedFamilies.Add((feld, gusFeld, _familyStandard, 2, 2));

        // F2 OKONKWO — Family Standard OVER size: 2A + 4K → +2 extra kids = $190.
        var ada = Cast("Ada", "Okonkwo", PersonRoles.Member, new DateOnly(1989, 9, 9), null, 2019);
        var chidi = Cast("Chidi", "Okonkwo", PersonRoles.Member, new DateOnly(1988, 1, 20), null, 2019);
        var okonkwoKids = new[]
        {
            Cast("Ngozi", "Okonkwo", PersonRoles.Member, _today.AddYears(-11), null, 2022, hasLogin: false),
            Cast("Obi", "Okonkwo", PersonRoles.Member, _today.AddYears(-9), null, 2022, hasLogin: false),
            Cast("Zina", "Okonkwo", PersonRoles.Member, _today.AddYears(-8), null, 2023, hasLogin: false),
            Cast("Kelechi", "Okonkwo", PersonRoles.Member, _today.AddYears(-6), null, 2024, hasLogin: false),
        };
        _bjjAdults.Add(ada);
        _mtAdults.Add(ada);
        _bjjAdults.Add(chidi);
        _kidsBjj.AddRange(okonkwoKids[..3]);
        _kidsJudo.Add(okonkwoKids[3]);
        var okonkwo = Family("OKONKWO FAMILY", ada.UserId!.Value, _familyStandard.Id,
            adults: [ada, chidi], wards: okonkwoKids);
        _billedFamilies.Add((okonkwo, ada, _familyStandard, 2, 4));

        // F3 VARGA — Family Per-Head: 1A + 2K on $0 base = $80 + $100 = $180.
        var reka = Cast("Reka", "Varga", PersonRoles.Member, new DateOnly(1992, 2, 2), null, 2021);
        var vargaKids = new[]
        {
            Cast("Zsofi", "Varga", PersonRoles.Member, _today.AddYears(-10), null, 2023, hasLogin: false),
            Cast("Bence", "Varga", PersonRoles.Member, _today.AddYears(-8), null, 2023, hasLogin: false),
        };
        _bjjAdults.Add(reka);
        _fitAdults.Add(reka);
        _kidsBjj.AddRange(vargaKids);
        var varga = Family("VARGA FAMILY", reka.UserId!.Value, _familyPerHead.Id,
            adults: [reka], wards: vargaKids);
        _billedFamilies.Add((varga, reka, _familyPerHead, 1, 2));

        // F4/F5 — the Leo shape ×2: ward on an INDIVIDUAL plan, family has no
        // family plan, guardian is a login with NO roster Person. Ward charges
        // route to the guardian (#198) and land on /dues/child (#199).
        _wardFinn = Cast("Finn", "Morrow", PersonRoles.Member, _today.AddYears(-10), _kidsBjjPlan.Id, 2024, hasLogin: false);
        _kidsBjj.Add(_wardFinn);
        _guardianFinnUserId = NewUser("dana.morrow");
        Family("MORROW FAMILY", _guardianFinnUserId, null, adults: [], wards: [_wardFinn]);

        var zoe = Cast("Zoe", "Baptiste", PersonRoles.Member, _today.AddYears(-11), _kidsJudoPlan.Id, 2025, hasLogin: false);
        _kidsJudo.Add(zoe);
        Family("BAPTISTE FAMILY", NewUser("remy.baptiste"), null, adults: [], wards: [zoe]);

        // F6 HOLT — the Sarah shape + a TEEN ward with their own login.
        var theo = Cast("Theo", "Holt", PersonRoles.Member, _today.AddYears(-16), _kidsBjjPlan.Id, 2023);
        _kidsBjj.Add(theo);
        Family("HOLT FAMILY", NewUser("mora.holt"), null, adults: [], wards: [theo]);

        // F7 ASHFORD — the bills-both TRAP: the primary's User is linked to a
        // Person who is NOT a family member; the family plan bills him AND his
        // own Adult plan bills him. Kept current so arrears stay pinned at 14.
        var bram = Cast("Bram", "Ashford", PersonRoles.Member, new DateOnly(1986, 6, 6), _adultPlan.Id, 2018);
        _bjjAdults.Add(bram);
        var ashfordKids = new[]
        {
            Cast("Wren", "Ashford", PersonRoles.Member, _today.AddYears(-9), null, 2023, hasLogin: false),
            Cast("Piet", "Ashford", PersonRoles.Member, _today.AddYears(-7), null, 2024, hasLogin: false),
        };
        _kidsBjj.AddRange(ashfordKids);
        var ashford = Family("ASHFORD FAMILY", bram.UserId!.Value, _familyStandard.Id, adults: [], wards: ashfordKids);
        _billedFamilies.Add((ashford, bram, _familyStandard, 0, 2));

        // F8 NAKAMURA — the 18+ ward (graduation-nudge banner).
        var kaiN = Cast("Kai", "Nakamura", PersonRoles.Member, _today.AddYears(-18).AddDays(-120), _adultPlan.Id, 2021);
        _bjjAdults.Add(kaiN);
        _judoAdults.Add(kaiN);
        Family("NAKAMURA FAMILY", NewUser("emi.nakamura"), null, adults: [], wards: [kaiN]);

        // ---- bulk roster to exactly 300 ----
        // Named so far: 12 base + 3 reds + 2 visitors + 2 archived + 20 family
        // persons = 39. Bulk = 261: 202 adults + 59 kids.
        AddBulkAdults(_bjjAdults, _adultPlan, 93, random);
        AddBulkAdults(_mtAdults, _mtPlan, 29, random);
        AddBulkAdults(_judoAdults, _judoPlan, 16, random);
        AddBulkAdults(_fitAdults, _fitPlan, 22, random);
        for (var i = 0; i < 42; i++) // mixed: BJJ + one other discipline
        {
            var person = BulkPerson(_adultPlan, new DateOnly(1978 + random.Next(28), 1 + random.Next(12), 1 + random.Next(28)), random, loginEvery: 2);
            _bjjAdults.Add(person);
            (i % 3 == 0 ? _mtAdults : i % 3 == 1 ? _judoAdults : _fitAdults).Add(person);
        }

        for (var i = 0; i < 37; i++)
        {
            _kidsBjj.Add(BulkPerson(_kidsBjjPlan, _today.AddYears(-(5 + random.Next(9))), random, loginEvery: 0));
        }
        for (var i = 0; i < 16; i++)
        {
            _kidsJudo.Add(BulkPerson(_kidsJudoPlan, _today.AddYears(-(6 + random.Next(8))), random, loginEvery: 0));
        }
        for (var i = 0; i < 6; i++) // both kids programs
        {
            var kid = BulkPerson(_kidsBjjPlan, _today.AddYears(-(7 + random.Next(7))), random, loginEvery: 0);
            _kidsBjj.Add(kid);
            _kidsJudo.Add(kid);
        }

        // Pinned arrears membership (named exemplars + deterministic bulk picks).
        _behind1.AddRange([_behindOneMonth, .. _bjjAdults.Where(IsBulk).Take(4), .. _mtAdults.Where(IsBulk).Take(2)]);
        _behind2.AddRange([.. _fitAdults.Where(IsBulk).Take(3), .. _judoAdults.Where(IsBulk).Take(2)]);
        _behind3.AddRange([_behindThreeMonths, _bjjAdults.Where(IsBulk).Skip(4).First()]);

        await db.SaveChangesAsync(ct);

        static bool IsBulk(Person p) => p.UserId is null && !p.Archived && !p.Visitor && p.DateOfBirth < new DateOnly(2000, 1, 1);

        void AddBulkAdults(List<Person> pool, MembershipPlan plan, int count, Random rnd)
        {
            for (var i = 0; i < count; i++)
            {
                pool.Add(BulkPerson(plan, new DateOnly(1978 + rnd.Next(28), 1 + rnd.Next(12), 1 + rnd.Next(28)), rnd, loginEvery: 2));
            }
        }

        Person BulkPerson(MembershipPlan plan, DateOnly dob, Random rnd, int loginEvery)
        {
            var (first, last) = NextName(rnd);
            var hasLogin = loginEvery > 0 && rnd.Next(loginEvery) == 0;
            return Cast(first, last, PersonRoles.Member, dob, plan.Id, 2015 + rnd.Next(11), hasLogin);
        }
    }

    private async Task SeedRanksAsync(CancellationToken ct)
    {
        var random = new Random(102);
        var adultLadder = await db.Ranks.Where(r => r.RankSystemId == IbjjfSeed.AdultSystemId).OrderBy(r => r.Order).ToListAsync(ct);
        var kidsLadder = await db.Ranks.Where(r => r.RankSystemId == IbjjfSeed.KidsSystemId).OrderBy(r => r.Order).ToListAsync(ct);

        var prajioud = CustomSystem("Muay Thai Prajioud",
        [
            ("White", "#E9E6DC", "#17181A"), ("Yellow", "#D9A62E", "#17181A"), ("Green", "#3E8E5A", "#17181A"),
            ("Blue", "#2456A6", "#17181A"), ("Red", "#A31D26", "#17181A"), ("Black", "#17181A", "#A31D26"),
        ]);
        var judoLadder = CustomSystem("Judo",
        [
            ("White", "#E9E6DC", "#17181A"), ("Yellow", "#D9A62E", "#17181A"), ("Orange", "#C9711F", "#17181A"),
            ("Green", "#3E8E5A", "#17181A"), ("Blue", "#2456A6", "#17181A"), ("Brown", "#6B4A2B", "#E9E6DC"),
            ("Black", "#17181A", "#E9E6DC"),
        ]);

        // Coaches wear their fiction.
        Award(_coachBjjHead, adultLadder[4], 3, new DateOnly(2016, 5, 14));
        Award(_coachBjjSecond, adultLadder[4], 0, new DateOnly(2024, 11, 2));
        Award(_coachMt, prajioud[5], 0, new DateOnly(2018, 2, 3), selfReported: true);
        Award(_coachJudo, judoLadder[6], 0, new DateOnly(2019, 9, 21), selfReported: true);

        // The three red belts: every top rank is held (the ranks board shows all).
        var reds = _bjjAdults.Where(p => p.LastName is "Pinto" or "Lobo" or "Sato").ToList();
        Award(reds[0], adultLadder[7], 0, new DateOnly(2015, 3, 1), selfReported: true);
        Award(reds[1], adultLadder[6], 0, new DateOnly(2017, 8, 12), selfReported: true);
        Award(reds[2], adultLadder[5], 0, new DateOnly(2019, 1, 5), selfReported: true);

        // Bulk BJJ adults spread White→Black; every rank ends up populated.
        var bulkBjj = _bjjAdults.Where(p => p.LastName is not ("Pinto" or "Lobo" or "Sato")).ToList();
        foreach (var (person, index) in bulkBjj.Select((p, i) => (p, i)))
        {
            var rank = adultLadder[Math.Min(index % 15 == 0 ? 4 : index / 34, 4)];
            Award(person, rank, random.Next(rank.MaxStripes + 1), _today.AddDays(-random.Next(60, 1200)));
        }

        // Secondary-discipline ranks, back-dated so BJJ stays each mixed member's
        // primary (roster bar) belt — the #140 back-dating trick.
        foreach (var (person, index) in _mtAdults.Where(p => p.Id != _coachMt.Id).Select((p, i) => (p, i)))
        {
            Award(person, prajioud[index % 5], 0, _today.AddDays(-random.Next(500, 1400)));
        }
        foreach (var (person, index) in _judoAdults.Where(p => p.Id != _coachJudo.Id).Select((p, i) => (p, i)))
        {
            Award(person, judoLadder[index % 6], 0, _today.AddDays(-random.Next(500, 1400)));
        }

        // Kids across all 13 IBJJF kids ranks; kids-judo kids across the Judo ladder.
        foreach (var (kid, index) in _kidsBjj.Select((p, i) => (p, i)))
        {
            var rank = kidsLadder[index % kidsLadder.Count];
            Award(kid, rank, random.Next(Math.Min(rank.MaxStripes, 4) + 1), _today.AddDays(-random.Next(30, 700)));
        }
        foreach (var (kid, index) in _kidsJudo.Where(k => !_kidsBjj.Contains(k)).Select((p, i) => (p, i)))
        {
            Award(kid, judoLadder[index % 4], 0, _today.AddDays(-random.Next(30, 700)));
        }

        List<Rank> CustomSystem(string systemName, (string Name, string Band, string Bar)[] belts)
        {
            var system = new RankSystem { Id = Guid.NewGuid(), GymId = _gym.Id, Name = systemName, IsSeeded = false };
            db.RankSystems.Add(system);
            var ranks = belts.Select((belt, index) => new Rank
            {
                Id = Guid.NewGuid(),
                RankSystemId = system.Id,
                Name = belt.Name,
                Order = index + 1,
                BandColorHex = belt.Band,
                BarColorHex = belt.Bar,
                MaxStripes = 0,
            }).ToList();
            db.Ranks.AddRange(ranks);
            return ranks;
        }

        void Award(Person person, Rank rank, int stripes, DateOnly on, bool selfReported = false)
        {
            db.RankAwards.Add(new RankAward
            {
                Id = Guid.NewGuid(),
                PersonId = person.Id,
                RankId = rank.Id,
                Stripes = stripes,
                AwardedOn = on,
                SelfReported = selfReported,
                AwardedByPersonId = selfReported ? null : _coachBjjHead.Id,
            });
        }
    }

    private async Task SeedScheduleHistoryAsync(CancellationToken ct)
    {
        var random = new Random(103);

        foreach (var template in _templates)
        {
            var pool = PoolFor(template);
            for (var weeksBack = 12; weeksBack >= 1; weeksBack--)
            {
                var date = _today.AddDays(-7 * weeksBack);
                date = date.AddDays((((int)template.Day - (int)date.DayOfWeek) + 7) % 7);
                if (date >= _today)
                {
                    continue;
                }

                var session = new ClassSession
                {
                    Id = Guid.NewGuid(),
                    TemplateId = template.Id,
                    Date = date,
                    StartTime = template.StartTime,
                    DurationMinutes = template.DurationMinutes,
                    Name = template.Name,
                    InstructorPersonId = InstructorFor(template),
                };
                db.ClassSessions.Add(session);

                // Claims are the correct record for backfilled weeks — not just
                // occupied-absorption (the demo's shortcut).
                db.ClassTemplateWeeks.Add(new ClassTemplateWeek
                {
                    Id = Guid.NewGuid(),
                    TemplateId = template.Id,
                    WeekStart = Weeks.WeekOf(date),
                });

                var turnout = Math.Min(pool.Count, (template.Name.StartsWith("Kids") ? 7 : 9) + random.Next(9));
                foreach (var person in pool.OrderBy(_ => random.Next()).Take(turnout))
                {
                    db.AttendanceRecords.Add(new AttendanceRecord
                    {
                        Id = Guid.NewGuid(),
                        SessionId = session.Id,
                        PersonId = person.Id,
                        Source = random.Next(5) == 0 ? CheckInSource.Instructor : CheckInSource.Self,
                        Status = AttendanceStatus.Confirmed,
                        ConfirmedUtc = DateTimeOffset.UtcNow,
                    });
                }
            }

            await db.SaveChangesAsync(ct); // chunk per template — ~5k attendance rows total
        }

        // A recent cancelled session (reason shows on the schedule)...
        var cancelled = new ClassSession
        {
            Id = Guid.NewGuid(),
            TemplateId = null,
            Date = _today.AddDays(-3),
            StartTime = new TimeOnly(20, 30),
            DurationMinutes = 60,
            Name = "Wrestling Guest Class",
            Status = SessionStatus.Cancelled,
            CancelledReason = "Guest coach's flight cancelled",
            InstructorPersonId = _coachBjjHead.Id,
        };
        db.ClassSessions.Add(cancelled);

        // ...an upcoming session with an OPEN substitution request (claim written
        // so next week's mint won't duplicate it)...
        var advGi = _templates.First(t => t.Name == "Adv Gi" && t.Day == DayOfWeek.Monday);
        var nextMonday = _today.AddDays(((((int)DayOfWeek.Monday - (int)_today.DayOfWeek) + 6) % 7) + 1);
        var needsCover = new ClassSession
        {
            Id = Guid.NewGuid(),
            TemplateId = advGi.Id,
            Date = nextMonday,
            StartTime = advGi.StartTime,
            DurationMinutes = advGi.DurationMinutes,
            Name = advGi.Name,
            InstructorPersonId = _coachBjjHead.Id,
        };
        db.ClassSessions.Add(needsCover);
        db.ClassTemplateWeeks.Add(new ClassTemplateWeek { Id = Guid.NewGuid(), TemplateId = advGi.Id, WeekStart = Weeks.WeekOf(nextMonday) });
        db.SubstitutionRequests.Add(new SubstitutionRequest
        {
            Id = Guid.NewGuid(),
            SessionId = needsCover.Id,
            RequestedByPersonId = _coachBjjHead.Id,
            ProposedSubPersonId = null, // open request — anyone can claim
        });

        // ...and a one-off (the promote-to-template candidate).
        db.ClassSessions.Add(new ClassSession
        {
            Id = Guid.NewGuid(),
            TemplateId = null,
            Date = _today.AddDays(5),
            StartTime = new TimeOnly(11, 0),
            DurationMinutes = 90,
            Name = "Leg Lock Lab",
            InstructorPersonId = _coachBjjSecond.Id,
        });

        List<Person> PoolFor(ClassTemplate template)
        {
            var tagNames = template.ClassTypes.Select(t => t.Name).ToHashSet();
            if (tagNames.Contains("kids"))
            {
                return tagNames.Contains("judo") ? _kidsJudo : _kidsBjj;
            }
            if (tagNames.Contains("muay-thai"))
            {
                return _mtAdults;
            }
            if (tagNames.Contains("judo"))
            {
                return _judoAdults;
            }
            if (tagNames.Contains("fitness"))
            {
                return _fitAdults;
            }
            return _bjjAdults;
        }

        Guid? InstructorFor(ClassTemplate template)
        {
            var tagNames = template.ClassTypes.Select(t => t.Name).ToHashSet();
            if (tagNames.Contains("muay-thai"))
            {
                return _coachMt.Id;
            }
            if (tagNames.Contains("judo") && !tagNames.Contains("kids"))
            {
                return _coachJudo.Id;
            }
            if (tagNames.Contains("fitness"))
            {
                return _coachFit.Id;
            }
            if (tagNames.Contains("kids"))
            {
                return tagNames.Contains("judo") ? _coachJudo.Id : _coachBjjSecond.Id;
            }
            return template.Name.StartsWith("Adv") ? _coachBjjHead.Id : _coachBjjSecond.Id;
        }
    }

    private void SeedLedger()
    {
        var random = new Random(104);
        var cycles = new[] { 2, 1, 0 }
            .Select(monthsBack => new DateOnly(_today.AddMonths(-monthsBack).Year, _today.AddMonths(-monthsBack).Month, 1))
            .ToList();

        var coveredPersonIds = _billedFamilies
            .SelectMany(f => f.Family.Members.Select(m => m.PersonId))
            .ToHashSet();

        var planById = db.MembershipPlans.Local.ToDictionary(p => p.Id);
        var billable = db.Persons.Local
            .Where(p => p.MembershipPlanId is { } planId
                && !p.Archived && !p.Visitor
                && !coveredPersonIds.Contains(p.Id)
                && planById[planId] is { Archived: false, Scope: PlanScope.PerPerson, Price: > 0 })
            .ToList();

        foreach (var (cycle, index) in cycles.Select((c, i) => (c, i)))
        {
            var monthsFromNewest = cycles.Count - 1 - index; // 2, 1, 0
            foreach (var person in billable)
            {
                var plan = planById[person.MembershipPlanId!.Value];
                db.Charges.Add(new Charge
                {
                    Id = Guid.NewGuid(),
                    PersonId = person.Id,
                    Amount = plan.Price,
                    Description = $"{plan.Name} · {cycle:yyyy-MM}",
                    RaisedOn = cycle,
                    CycleKey = $"{cycle:yyyy-MM}",
                });

                // Behind-N means the newest N cycles are unpaid; everyone else pays all.
                var unpaidCycles = _behind3.Contains(person) ? 3 : _behind2.Contains(person) ? 2 : _behind1.Contains(person) ? 1 : 0;
                if (monthsFromNewest >= unpaidCycles)
                {
                    db.Payments.Add(new Payment
                    {
                        Id = Guid.NewGuid(),
                        PersonId = person.Id,
                        Amount = plan.Price,
                        ReceivedOn = cycle.AddDays(1 + random.Next(6)),
                    });
                }
            }

            // Archived pair: history only — billed and settled in the two older
            // cycles, invisible to the newest (the cycle skips archived people).
            if (monthsFromNewest >= 1)
            {
                foreach (var person in _archivedMembers)
                {
                    db.Charges.Add(new Charge
                    {
                        Id = Guid.NewGuid(),
                        PersonId = person.Id,
                        Amount = 85m,
                        Description = $"Adult Unlimited · {cycle:yyyy-MM}",
                        RaisedOn = cycle,
                        CycleKey = $"{cycle:yyyy-MM}",
                    });
                    db.Payments.Add(new Payment { Id = Guid.NewGuid(), PersonId = person.Id, Amount = 85m, ReceivedOn = cycle.AddDays(3) });
                }
            }

            // Family charges: computed totals with the #181 breakdown stamped;
            // primaries stay current (arrears stay pinned to the individual 14).
            foreach (var (family, primary, plan, adults, kidCount) in _billedFamilies)
            {
                var (total, extra) = FamilyPlanMath.Compute(plan, adults, kidCount);
                if (total <= 0)
                {
                    continue;
                }

                db.Charges.Add(new Charge
                {
                    Id = Guid.NewGuid(),
                    PersonId = primary.Id,
                    Amount = total,
                    Description = $"{plan.Name} · {family.Name} · {cycle:yyyy-MM}",
                    RaisedOn = cycle,
                    CycleKey = $"{cycle:yyyy-MM}:family:{family.Id}",
                    FamilyAdults = adults,
                    FamilyKids = kidCount,
                    FamilyExtraAmount = extra,
                });
                db.Payments.Add(new Payment { Id = Guid.NewGuid(), PersonId = primary.Id, Amount = total, ReceivedOn = cycle.AddDays(2 + random.Next(4)) });
            }
        }

        // Expenses + income + one recurring expense (the worker has something real).
        var lastMonth = new DateOnly(_today.AddMonths(-1).Year, _today.AddMonths(-1).Month, 5);
        var categories = db.ExpenseCategories.Local.ToList();
        foreach (var (categoryName, amount) in new (string, decimal)[] { ("RENT", 5200m), ("INSURANCE", 510m), ("SOFTWARE", 240m), ("UTILITIES", 700m), ("MARKETING", 400m) })
        {
            db.Expenses.Add(new Expense
            {
                Id = Guid.NewGuid(),
                CategoryId = categories.Single(c => c.Name == categoryName).Id,
                Amount = amount,
                SpentOn = lastMonth,
            });
        }

        db.RecurringExpenses.Add(new RecurringExpense
        {
            Id = Guid.NewGuid(),
            CategoryId = categories.Single(c => c.Name == "RENT").Id,
            Amount = 5200m,
            DayOfMonth = 1,
            Active = true,
            Note = "Lease through 2028",
            LastMaterializedMonth = new DateOnly(_today.Year, _today.Month, 1),
        });

        db.OtherIncomes.Add(new OtherIncome { Id = Guid.NewGuid(), Label = "SEMINAR", Amount = 620m, ReceivedOn = lastMonth.AddDays(9), Note = "Guest wrestling clinic" });
    }

    private void SeedComms()
    {
        var random = new Random(105);

        var seminar = new GymEvent
        {
            Id = Guid.NewGuid(),
            Title = "Timed Seminar: Wrestling for BJJ",
            Kind = GymEventKind.Seminar,
            StartsOn = _today.AddDays(10),
            StartTime = new TimeOnly(11, 0),
            DurationMinutes = 180,
            TimeInfo = "11:00–14:00",
            Location = "Main mat",
            Details = "$35 members — cap 40.",
            PublishedByPersonId = _coachBjjHead.Id,
        };
        var tournament = new GymEvent
        {
            Id = Guid.NewGuid(),
            Title = "Regional Open — All Disciplines",
            Kind = GymEventKind.Tournament,
            StartsOn = _today.AddDays(20),
            Location = "Fieldhouse East",
            Details = "Team registration closes a week out.",
            PublishedByPersonId = _owner.Id,
        };
        db.GymEvents.AddRange(seminar, tournament);

        foreach (var person in _bjjAdults.Where(p => p.UserId != null).Take(15))
        {
            db.EventRsvps.Add(new EventRsvp
            {
                Id = Guid.NewGuid(),
                EventId = random.Next(3) == 0 ? tournament.Id : seminar.Id,
                PersonId = person.Id,
                Status = random.Next(4) == 0 ? RsvpStatus.Interested : RsvpStatus.Going,
            });
        }

        // Unread contact messages (MESSAGES badge lights up on day one).
        db.ContactMessages.AddRange(
            new ContactMessage { Id = Guid.NewGuid(), FirstName = "Robin", LastName = "Tsai", Email = "robin.tsai@example.test", Body = "Do you run a beginners Judo intro? My son is 9.", CreatedUtc = DateTimeOffset.UtcNow.AddHours(-20) },
            new ContactMessage { Id = Guid.NewGuid(), FirstName = "Max", LastName = "Oduya", Phone = "+1 555 0138", Body = "Looking for morning fitness options before work.", CreatedUtc = DateTimeOffset.UtcNow.AddHours(-6) });

        // Notifications spread: staff + members, mixed read state — the inbox
        // filters and both badges have real data immediately.
        Note(_owner.UserId!.Value, NotificationCategory.ContactMessageReceived, "New contact message from Robin Tsai", "Do you run a beginners Judo intro? My son is 9.", "/admin/messages", hoursAgo: 20, read: false);
        Note(_owner.UserId!.Value, NotificationCategory.ContactMessageReceived, "New contact message from Max Oduya", "Looking for morning fitness options before work.", "/admin/messages", hoursAgo: 6, read: false);
        Note(_coachBjjSecond.UserId!.Value, NotificationCategory.SwapRequested, "Cover needed: Adv Gi · Monday", "Mateus needs cover for Monday's Adv Gi — open claim.", "/schedule", hoursAgo: 30, read: false);
        Note(_behindOneMonth.UserId!.Value, NotificationCategory.ChargeRaised, $"Dues raised: Adult Unlimited · {_today:yyyy-MM}", "Your Adult Unlimited dues were raised.", "/dues", hoursAgo: 72, read: false);
        Note(_behindThreeMonths.UserId!.Value, NotificationCategory.ChargeRaised, $"Dues raised: Adult Unlimited · {_today:yyyy-MM}", "Your Adult Unlimited dues were raised.", "/dues", hoursAgo: 72, read: true);
        Note(_guardianFinnUserId, NotificationCategory.ChargeRaised, $"Dues raised: Finn Morrow · Kids BJJ · {_today:yyyy-MM}", "Finn Morrow's Kids BJJ dues were raised.", $"/dues/child/{_wardFinn.Id}", hoursAgo: 72, read: false);

        void Note(Guid recipientUserId, NotificationCategory category, string title, string body, string link, int hoursAgo, bool read)
        {
            db.Notifications.Add(new Notification
            {
                Id = Guid.NewGuid(),
                RecipientUserId = recipientUserId,
                Category = category,
                Title = title,
                Body = body,
                LinkPath = link,
                CreatedUtc = DateTimeOffset.UtcNow.AddHours(-hoursAgo),
                ReadUtc = read ? DateTimeOffset.UtcNow.AddHours(-hoursAgo + 1) : null,
            });
        }
    }

    // ---------- shared construction helpers ----------

    private Family Family(string familyName, Guid primaryGuardianUserId, Guid? planId, Person[] adults, Person[] wards)
    {
        var family = new Family { Id = Guid.NewGuid(), Name = familyName, MembershipPlanId = planId };
        db.Families.Add(family);
        foreach (var adult in adults)
        {
            var membership = new FamilyMember { Id = Guid.NewGuid(), FamilyId = family.Id, PersonId = adult.Id, IsWard = false };
            family.Members.Add(membership);
            db.FamilyMembers.Add(membership);
        }
        foreach (var ward in wards)
        {
            var membership = new FamilyMember { Id = Guid.NewGuid(), FamilyId = family.Id, PersonId = ward.Id, IsWard = true };
            family.Members.Add(membership);
            db.FamilyMembers.Add(membership);
        }
        db.FamilyGuardians.Add(new FamilyGuardian
        {
            Id = Guid.NewGuid(),
            FamilyId = family.Id,
            GuardianUserId = primaryGuardianUserId,
            IsPrimary = true,
            ActForWards = true,
            ManageGuardians = true,
            ManageMembers = true,
            ViewBilling = true,
        });
        return family;
    }

    private Guid NewUser(string handle)
    {
        if (!_usedHandles.Add(handle))
        {
            handle = $"{handle}{_usedHandles.Count}";
            _usedHandles.Add(handle);
        }

        var id = Guid.NewGuid();
        db.Users.Add(new Identity.AppUser
        {
            Id = id,
            UserName = $"{handle}@{_slug}.demo",
            Email = $"{handle}@{_slug}.demo",
            NormalizedUserName = $"{handle}@{_slug}.demo".ToUpperInvariant(),
            NormalizedEmail = $"{handle}@{_slug}.demo".ToUpperInvariant(),
            EmailConfirmed = true,
        });
        return id;
    }

    private Person Cast(string first, string last, PersonRoles roles, DateOnly dob, Guid? planId, int joinedYear, bool hasLogin = true)
    {
        _usedNames.Add($"{first} {last}");
        var person = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = first,
            LastName = last,
            Roles = roles,
            DateOfBirth = dob,
            MembershipPlanId = planId,
            JoinedOn = new DateOnly(Math.Min(joinedYear, _today.Year), 3, 15),
            UserId = hasLogin ? NewUser($"{first}.{last}".ToLowerInvariant()) : null,
        };
        db.Persons.Add(person);
        return person;
    }

    private (string First, string Last) NextName(Random random)
    {
        for (var attempt = 0; attempt < 500; attempt++)
        {
            var first = FirstNames[random.Next(FirstNames.Length)];
            var last = LastNames[random.Next(LastNames.Length)];
            if (_usedNames.Add($"{first} {last}"))
            {
                return (first, last);
            }
        }

        var suffix = _usedNames.Count;
        var fallbackFirst = FirstNames[suffix % FirstNames.Length];
        var fallbackLast = $"{LastNames[suffix % LastNames.Length]}{suffix}";
        _usedNames.Add($"{fallbackFirst} {fallbackLast}");
        return (fallbackFirst, fallbackLast);
    }
}
