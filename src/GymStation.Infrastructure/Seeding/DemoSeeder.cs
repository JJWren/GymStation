using GymStation.Domain.Attendance;
using GymStation.Domain.Events;
using GymStation.Domain.Marketing;
using GymStation.Domain.Money;
using GymStation.Domain.People;
using GymStation.Domain.Ranks;
using GymStation.Domain.Scheduling;
using GymStation.Domain.Tenancy;
using GymStation.Infrastructure.Ranks;
using GymStation.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace GymStation.Infrastructure.Seeding;

/// <summary>
/// Builds the pitch-demo tenant: a realistic fictional academy with a full cast, twelve
/// weeks of attendance, a live ledger, and events. The cast and story are deterministic
/// (fixed Random seed) relative to the seeding date — dates anchor to "today" so the demo
/// always looks current. Refuses to run if the slug already exists.
/// </summary>
public class DemoSeeder(GymStationDbContext db, TenantContext tenant)
{
    private static readonly string[] FirstNames =
        ["Sam", "Priya", "Jade", "Ben", "Nina", "Diego", "Maya", "Chris", "Aisha", "Victor",
         "Elena", "Kofi", "Rosa", "Jan", "Tariq", "Ivy", "Marco", "Sana", "Pete", "Lucia",
         "Owen", "Fatima", "Hugo", "Wren", "Talia", "Ray", "Noor", "Felix", "Dara", "Miles"];

    private static readonly string[] LastNames =
        ["Ortiz", "Nair", "Kim", "Foster", "Sousa", "Alves", "Cole", "Yuen", "Bello", "Cruz",
         "Novak", "Mensah", "Reis", "Kowal", "Aziz", "Stone", "Rossi", "Iqbal", "Larsen", "Vega",
         "Boone", "Zahra", "Brandt", "Ashby", "Moss", "Quinn", "Haddad", "Lang", "Okoye", "Pratt"];

    public async Task<Guid> SeedAsync(string slug, string name, CancellationToken ct = default)
    {
        if (await db.Gyms.AnyAsync(g => g.Slug == slug, ct))
        {
            throw new InvalidOperationException($"A gym with slug '{slug}' already exists — demo seeding refuses to touch it.");
        }

        var random = new Random(42);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var gym = new Gym { Id = Guid.NewGuid(), Name = name, Slug = slug, TimeZoneId = "America/Chicago" };
        db.Gyms.Add(gym);
        await db.SaveChangesAsync(ct);

        tenant.SetGym(gym.Id);
        db.GymSettings.Add(new GymSettings
        {
            GymId = gym.Id,
            SubstitutionMode = SubstitutionMode.AdminGate,
            AboutText = $"""
                **Founded in 2014**, {name} started as six borrowed mats in a warehouse bay and grew into the Gulf Coast's home for grapplers, strikers, and anyone chasing a harder-to-kill version of themselves.

                Three programs, one room, zero ego:

                - **Brazilian Jiu-Jitsu** — gi and no-gi, fundamentals through black belt
                - **Bootcamp** — strength and conditioning, no experience needed
                - **Muay Thai** — the art of eight limbs, technique-first

                First class is always free. Come watch, come train, come say hi.
                """,
            ProgramsIntro = "Three doors into the same room — pick your pace, switch anytime. Every adult membership includes all three.",
        });

        foreach (var categoryName in new[] { "RENT", "INSURANCE", "SOFTWARE", "UTILITIES", "MARKETING" })
        {
            db.ExpenseCategories.Add(new ExpenseCategory { Id = Guid.NewGuid(), Name = categoryName });
        }

        // Plans.
        var adultPlan = new MembershipPlan { Id = Guid.NewGuid(), Name = "Adult Unlimited", Price = 85m };
        var kidsPlan = new MembershipPlan { Id = Guid.NewGuid(), Name = "Kids BJJ", Price = 65m };
        db.MembershipPlans.AddRange(adultPlan, kidsPlan);

        // Class tags.
        var gi = Tag("gi", "#C9503B");
        var noGi = Tag("no-gi", "#3E8E9E");
        var kids = Tag("kids", "#3E8E5A");
        var fundamentals = Tag("fundamentals", "#C9A227");
        var competition = Tag("competition", "#B23B48");
        var openMat = Tag("open-mat", "#707886");
        var bootcamp = Tag("bootcamp", "#B0622F");
        var muayThai = Tag("muay-thai", "#4A6FA5");

        // The named cast (from the Academy Ledger design fiction).
        var torres = Cast("Jordan", "Torres", PersonRoles.Owner | PersonRoles.Admin, new DateOnly(1985, 7, 1), null, 2014);
        var silva = Cast("Rui", "Silva", PersonRoles.Instructor | PersonRoles.Member, new DateOnly(1982, 3, 4), null, 2014);
        var ana = Cast("Ana", "Duarte", PersonRoles.Instructor | PersonRoles.Member, new DateOnly(1990, 9, 12), null, 2018);
        var dana = Cast("Dana", "Okafor", PersonRoles.Instructor | PersonRoles.Member, new DateOnly(1994, 1, 25), adultPlan.Id, 2021);
        var chai = Cast("Chai", "Rattana", PersonRoles.Instructor | PersonRoles.Member, new DateOnly(1987, 11, 8), null, 2024);
        var reyes = Cast("Ana", "Reyes", PersonRoles.Member, new DateOnly(1996, 12, 25), adultPlan.Id, 2021);
        var kim = Cast("Priya", "Kim", PersonRoles.Staff, new DateOnly(1998, 5, 30), null, 2023);
        var webb = Cast("Marcus", "Webb", PersonRoles.Member, new DateOnly(1993, 6, 2), adultPlan.Id, 2024);
        var omar = Cast("Omar", "Haddad", PersonRoles.Member, new DateOnly(1988, 4, 17), adultPlan.Id, 2019);
        var tom = Cast("Tom", "Hale", PersonRoles.Member, today.AddYears(-15), adultPlan.Id, 2025);
        var leo = Cast("Leo", "Park", PersonRoles.Member, today.AddYears(-8), kidsPlan.Id, 2026, hasLogin: false);

        // Two single-guardian families (#89): the primary holds every flag.
        AddFamily("HALE FAMILY", NewUser("sarah.hale"), tom);
        AddFamily("PARK FAMILY", NewUser("jin.park"), leo);

        // Fill the roster to ~50: 30 generated adults + 12 generated kids.
        var generatedAdults = new List<Person>();
        for (var i = 0; i < 30; i++)
        {
            var person = Cast(FirstNames[i], LastNames[i], PersonRoles.Member,
                new DateOnly(1980 + random.Next(28), 1 + random.Next(12), 1 + random.Next(28)),
                adultPlan.Id, 2020 + random.Next(6), hasLogin: i % 3 != 0);
            generatedAdults.Add(person);
        }

        var generatedKids = new List<Person>();
        for (var i = 0; i < 12; i++)
        {
            generatedKids.Add(Cast(FirstNames[29 - i], LastNames[i], PersonRoles.Member,
                today.AddYears(-(6 + random.Next(9))), kidsPlan.Id, 2024 + random.Next(3), hasLogin: false));
        }

        await db.SaveChangesAsync(ct);

        // Ranks: named cast get their design-fiction belts; generated adults spread by seniority.
        var adult = await db.Ranks.Where(r => r.RankSystemId == IbjjfSeed.AdultSystemId).OrderBy(r => r.Order).ToListAsync(ct);
        var kidsLadder = await db.Ranks.Where(r => r.RankSystemId == IbjjfSeed.KidsSystemId).OrderBy(r => r.Order).ToListAsync(ct);

        Award(silva, adult[4], 2, new DateOnly(2018, 6, 9));
        Award(ana, adult[4], 1, new DateOnly(2023, 3, 18));
        Award(dana, adult[3], 1, new DateOnly(2024, 10, 5));
        Award(reyes, adult[1], 0, new DateOnly(2021, 3, 21), selfReported: true);
        Award(reyes, adult[2], 0, new DateOnly(2024, 12, 2));
        Award(reyes, adult[2], 2, new DateOnly(2026, 6, 14));
        Award(webb, adult[1], 3, new DateOnly(2025, 8, 30));
        Award(omar, adult[2], 4, new DateOnly(2023, 2, 11));
        Award(tom, adult[0], 2, new DateOnly(2026, 2, 7));
        // kidsLadder[2] = Grey (index 0 is now the kids White belt): Leo stays grey.
        Award(leo, kidsLadder[2], 1, new DateOnly(2026, 5, 16));

        foreach (var (person, index) in generatedAdults.Select((p, i) => (p, i)))
        {
            var rank = adult[Math.Min(index / 8, 3)];
            Award(person, rank, random.Next(rank.MaxStripes + 1), today.AddDays(-random.Next(90, 900)));
        }

        // One kid per kids-ladder belt — the full 12-belt system shows on the ranks board.
        // Own Random: consuming the shared sequence would shift every downstream draw
        // and break the tuned ledger fiction (exactly $510 behind across 6 members).
        var kidBeltRandom = new Random(7);
        foreach (var (kid, index) in generatedKids.Select((p, i) => (p, i)))
        {
            var rank = kidsLadder[Math.Min(index, kidsLadder.Count - 1)];
            Award(kid, rank, kidBeltRandom.Next(Math.Min(rank.MaxStripes, 4) + 1), today.AddDays(-kidBeltRandom.Next(60, 700)));
        }

        // The gym's own Muay Thai ladder (#140) — a CUSTOM system exactly as the
        // #139 UI would create it: GymId set, not seeded, armbands don't stripe.
        var prajioud = new RankSystem { Id = Guid.NewGuid(), GymId = gym.Id, Name = "Muay Thai Prajioud", IsSeeded = false };
        db.RankSystems.Add(prajioud);
        var armbands = new (string Name, string Band, string Bar)[]
        {
            ("White", "#E9E6DC", "#17181A"),
            ("Yellow", "#D9A62E", "#17181A"),
            ("Green", "#3E8E5A", "#17181A"),
            ("Blue", "#2456A6", "#17181A"),
            ("Red", "#A31D26", "#17181A"),
            ("Black", "#17181A", "#A31D26"),
        };
        var prajioudRanks = armbands.Select((band, index) => new Rank
        {
            Id = Guid.NewGuid(),
            RankSystemId = prajioud.Id,
            Name = band.Name,
            Order = index + 1,
            BandColorHex = band.Band,
            BarColorHex = band.Bar,
            MaxStripes = 0,
        }).ToList();
        db.Ranks.AddRange(prajioudRanks);

        // Cross-training history: dated BEFORE each member's newest BJJ award so
        // their primary (roster) rank stays the design-fiction belt. Chai's black
        // armband is his only rank — his bar shows the Muay Thai ladder.
        Award(chai, prajioudRanks[5], 0, new DateOnly(2019, 4, 6), selfReported: true);
        Award(webb, prajioudRanks[1], 0, new DateOnly(2025, 5, 10), by: chai.Id);
        Award(omar, prajioudRanks[2], 0, new DateOnly(2022, 9, 17), by: chai.Id);

        // Weekly grid (the design week).
        var templates = new[]
        {
            Template("Fundamentals", DayOfWeek.Monday, new TimeOnly(6, 0), 60, silva.Id, gi, fundamentals),
            Template("Adv Gi", DayOfWeek.Monday, new TimeOnly(18, 0), 90, silva.Id, gi),
            Template("No-Gi Lunch", DayOfWeek.Tuesday, new TimeOnly(12, 0), 60, ana.Id, noGi),
            Template("No-Gi", DayOfWeek.Tuesday, new TimeOnly(18, 0), 90, ana.Id, noGi),
            Template("Kids BJJ", DayOfWeek.Wednesday, new TimeOnly(17, 0), 45, dana.Id, kids),
            Template("Adv Gi", DayOfWeek.Wednesday, new TimeOnly(18, 0), 90, silva.Id, gi),
            Template("Fundamentals", DayOfWeek.Thursday, new TimeOnly(18, 0), 60, dana.Id, gi, fundamentals),
            Template("Open Mat", DayOfWeek.Friday, new TimeOnly(18, 0), 120, null, openMat),
            Template("Competition", DayOfWeek.Saturday, new TimeOnly(10, 0), 90, silva.Id, competition),
        };

        // Bootcamp + Muay Thai grid (#140). A SEPARATE array on purpose: the main
        // 12-week loop below draws from the shared Random, and its draw count is
        // part of the tuned ledger fiction — these templates get their own
        // history loop with their own Random instead.
        var crossTraining = new[]
        {
            Template("Bootcamp", DayOfWeek.Tuesday, new TimeOnly(6, 0), 45, dana.Id, bootcamp),
            Template("Bootcamp", DayOfWeek.Thursday, new TimeOnly(6, 0), 45, dana.Id, bootcamp),
            Template("Bootcamp Circuits", DayOfWeek.Saturday, new TimeOnly(9, 0), 45, dana.Id, bootcamp),
            Template("Muay Thai", DayOfWeek.Monday, new TimeOnly(19, 30), 60, chai.Id, muayThai),
            Template("Muay Thai", DayOfWeek.Wednesday, new TimeOnly(19, 30), 60, chai.Id, muayThai),
            Template("Muay Thai Sparring", DayOfWeek.Friday, new TimeOnly(17, 0), 60, chai.Id, muayThai),
        };
        await db.SaveChangesAsync(ct);

        // Twelve weeks of sessions + confirmed attendance (deterministic turnout).
        var adults = generatedAdults.Concat([reyes, webb, omar, tom, dana]).ToList();
        for (var weeksBack = 12; weeksBack >= 1; weeksBack--)
        {
            var monday = today.AddDays(-7 * weeksBack - (((int)today.DayOfWeek + 6) % 7));
            foreach (var template in templates)
            {
                var date = monday.AddDays(((int)template.Day + 6) % 7);
                var session = new ClassSession
                {
                    Id = Guid.NewGuid(),
                    TemplateId = template.Id,
                    Date = date,
                    StartTime = template.StartTime,
                    DurationMinutes = template.DurationMinutes,
                    Name = template.Name,
                    InstructorPersonId = template.DefaultInstructorPersonId,
                };
                db.ClassSessions.Add(session);

                var pool = template.Name == "Kids BJJ" ? [leo] : adults;
                var turnout = Math.Min(pool.Count, 6 + random.Next(9));
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
        }

        // Cross-training turnout — own Random (the Random(7) precedent): touching
        // the shared sequence here would reshuffle the behind-set draw below and
        // could pull Webb into it, collapsing $510 to $425.
        var crossRandom = new Random(11);
        for (var weeksBack = 12; weeksBack >= 1; weeksBack--)
        {
            var monday = today.AddDays(-7 * weeksBack - (((int)today.DayOfWeek + 6) % 7));
            foreach (var template in crossTraining)
            {
                var date = monday.AddDays(((int)template.Day + 6) % 7);
                var session = new ClassSession
                {
                    Id = Guid.NewGuid(),
                    TemplateId = template.Id,
                    Date = date,
                    StartTime = template.StartTime,
                    DurationMinutes = template.DurationMinutes,
                    Name = template.Name,
                    InstructorPersonId = template.DefaultInstructorPersonId,
                };
                db.ClassSessions.Add(session);

                var turnout = Math.Min(adults.Count, 4 + crossRandom.Next(7));
                foreach (var person in adults.OrderBy(_ => crossRandom.Next()).Take(turnout))
                {
                    db.AttendanceRecords.Add(new AttendanceRecord
                    {
                        Id = Guid.NewGuid(),
                        SessionId = session.Id,
                        PersonId = person.Id,
                        Source = crossRandom.Next(5) == 0 ? CheckInSource.Instructor : CheckInSource.Self,
                        Status = AttendanceStatus.Confirmed,
                        ConfirmedUtc = DateTimeOffset.UtcNow,
                    });
                }
            }
        }

        // Ledger: two billed cycles; six adults (incl. Webb) are one month behind = $510.
        var billed = adults.Where(p => p.MembershipPlanId == adultPlan.Id).ToList();
        var behind = billed.OrderBy(_ => random.Next()).Take(5).Concat([webb]).Distinct().Take(6).ToHashSet();
        foreach (var month in new[] { today.AddMonths(-1), today })
        {
            var cycleStart = new DateOnly(month.Year, month.Month, 1);
            foreach (var person in billed)
            {
                db.Charges.Add(new Charge
                {
                    Id = Guid.NewGuid(),
                    PersonId = person.Id,
                    Amount = adultPlan.Price,
                    Description = $"{adultPlan.Name} · {cycleStart:yyyy-MM}",
                    RaisedOn = cycleStart,
                    CycleKey = $"{cycleStart:yyyy-MM}",
                });

                var paysThisMonth = cycleStart.Month != today.Month || !behind.Contains(person);
                if (paysThisMonth)
                {
                    db.Payments.Add(new Payment
                    {
                        Id = Guid.NewGuid(),
                        PersonId = person.Id,
                        Amount = adultPlan.Price,
                        ReceivedOn = cycleStart.AddDays(1 + random.Next(6)),
                    });
                }
            }
        }

        // Last month's expense log — the break-even prefill inputs.
        var lastMonth = new DateOnly(today.AddMonths(-1).Year, today.AddMonths(-1).Month, 5);
        var categories = await db.ExpenseCategories.ToListAsync(ct);
        foreach (var (categoryName, amount) in new (string, decimal)[] { ("RENT", 4200m), ("INSURANCE", 450m), ("SOFTWARE", 180m), ("UTILITIES", 620m), ("MARKETING", 350m) })
        {
            db.Expenses.Add(new Expense
            {
                Id = Guid.NewGuid(),
                CategoryId = categories.Single(c => c.Name == categoryName).Id,
                Amount = amount,
                SpentOn = lastMonth,
            });
        }

        // Other income — the non-dues money the FINANCE page tracks (#88).
        db.OtherIncomes.AddRange(
            new OtherIncome { Id = Guid.NewGuid(), Label = "SEMINAR", Amount = 480m, ReceivedOn = lastMonth.AddDays(7), Note = "Guest black belt leg-lock seminar" },
            new OtherIncome { Id = Guid.NewGuid(), Label = "MERCH", Amount = 145m, ReceivedOn = lastMonth.AddDays(12) });

        // Events.
        db.GymEvents.AddRange(
            new GymEvent { Id = Guid.NewGuid(), Title = "Coastline Open — Gi & No-Gi", Kind = GymEventKind.Tournament, StartsOn = today.AddDays(16), Location = "Gulfport Sportsplex", Details = "Registration closes two weeks out. Team carpool from the gym.", PublishedByPersonId = silva.Id },
            new GymEvent { Id = Guid.NewGuid(), Title = "Leg-lock Fundamentals Seminar", Kind = GymEventKind.Seminar, StartsOn = today.AddDays(24), TimeInfo = "11:00–14:00", Location = name, Details = "$40 members — guest black belt.", PublishedByPersonId = silva.Id },
            new GymEvent { Id = Guid.NewGuid(), Title = "Quarterly promotion day", Kind = GymEventKind.Grading, StartsOn = today.AddDays(58), TimeInfo = "After competition class", PublishedByPersonId = silva.Id });

        db.StaffProfiles.AddRange(
            new StaffProfile { PersonId = silva.Id, ExperienceSummary = "Head coach · 20+ yrs on the mat", Bio = "2nd degree black belt. Fundamentals-first.", PayRate = 45m, PayRateUnit = PayRateUnit.PerClass },
            new StaffProfile { PersonId = ana.Id, ExperienceSummary = "No-gi program · 1st degree black", PayRate = 40m, PayRateUnit = PayRateUnit.PerClass },
            new StaffProfile { PersonId = dana.Id, ExperienceSummary = "Kids program & Bootcamp · brown belt", PayRate = 35m, PayRateUnit = PayRateUnit.PerClass },
            new StaffProfile { PersonId = chai.Id, ExperienceSummary = "Muay Thai program · Kru, 60+ fights", Bio = "Came up through the camps of Khon Kaen. Technique-first: footwork and balance before power — sparring is earned, never assumed.", PayRate = 40m, PayRateUnit = PayRateUnit.PerClass },
            new StaffProfile { PersonId = kim.Id, ExperienceSummary = "Front desk & ops", PayRate = 1400m, PayRateUnit = PayRateUnit.Monthly });

        // The public marketing page (#140): programs and stories reference the
        // real seeded grid; image slots stay empty for live upload at the pitch.
        db.GymPrograms.AddRange(
            new GymProgram
            {
                Id = Guid.NewGuid(),
                Title = "Brazilian Jiu-Jitsu",
                SortOrder = 1,
                Description = """
                    Gi and no-gi grappling for every level — a **fundamentals** track that assumes nothing, advanced classes that hold nothing back, and a kids program that builds calm, confident problem-solvers.

                    - Fundamentals: Monday & Thursday
                    - Advanced gi: Monday & Wednesday
                    - No-gi: Tuesday (lunch and evening)
                    - Kids: Wednesday · Open mat: Friday
                    """,
            },
            new GymProgram
            {
                Id = Guid.NewGuid(),
                Title = "Bootcamp",
                SortOrder = 2,
                Description = """
                    Forty-five minutes, zero technique barrier — kettlebells, sleds, intervals, and the occasional tire. Built to make your first BJJ or Muay Thai round feel easier, or to stand alone as the hardest workout of your week.

                    - Tuesday & Thursday, 6:00 AM
                    - Saturday circuits, 9:00 AM
                    """,
            },
            new GymProgram
            {
                Id = Guid.NewGuid(),
                Title = "Muay Thai",
                SortOrder = 3,
                Description = """
                    The art of eight limbs, taught technique-first: footwork, balance, and clean mechanics before power. Pad rounds every class; sparring is invite-based and controlled.

                    - Monday & Wednesday evenings
                    - Friday sparring (invite)
                    """,
            });

        db.SuccessStories.AddRange(
            new SuccessStory { Id = Guid.NewGuid(), SortOrder = 1, AttributedTo = "Marcus W., blue belt", Body = "Walked in to lose a little weight; eighteen months later I've got a blue belt, twenty fewer pounds, and a Tuesday crew I'd run through a wall for." },
            new SuccessStory { Id = Guid.NewGuid(), SortOrder = 2, AttributedTo = "Ana R., purple belt", Body = "I came for self-defense and stayed for the chess. Five years in, purple belt now, and still learning something every single round." },
            new SuccessStory { Id = Guid.NewGuid(), SortOrder = 3, AttributedTo = "Sarah H., gym parent", Body = "My teenager walked in shy and walked out of his first tournament with his hand raised. The coaches here teach a lot more than armbars." });

        await db.SaveChangesAsync(ct);
        tenant.Clear();
        _ = torres;
        return gym.Id;

        void AddFamily(string familyName, Guid guardianUserId, Person ward)
        {
            var family = new Family { Id = Guid.NewGuid(), Name = familyName };
            db.Families.Add(family);
            db.FamilyMembers.Add(new FamilyMember { Id = Guid.NewGuid(), FamilyId = family.Id, PersonId = ward.Id, IsWard = true });
            db.FamilyGuardians.Add(new FamilyGuardian
            {
                Id = Guid.NewGuid(),
                FamilyId = family.Id,
                GuardianUserId = guardianUserId,
                IsPrimary = true,
                ActForWards = true,
                ManageGuardians = true,
                ManageMembers = true,
                ViewBilling = true,
            });
        }

        ClassType Tag(string tagName, string color)
        {
            var tag = new ClassType { Id = Guid.NewGuid(), Name = tagName, ColorHex = color };
            db.ClassTypes.Add(tag);
            return tag;
        }

        Guid NewUser(string handle)
        {
            var id = Guid.NewGuid();
            db.Users.Add(new Identity.AppUser
            {
                Id = id,
                UserName = $"{handle}@{slug}.demo",
                Email = $"{handle}@{slug}.demo",
                NormalizedUserName = $"{handle}@{slug}.demo".ToUpperInvariant(),
                NormalizedEmail = $"{handle}@{slug}.demo".ToUpperInvariant(),
                EmailConfirmed = true,
            });
            return id;
        }

        Person Cast(string first, string last, PersonRoles roles, DateOnly dob, Guid? planId, int joinedYear, bool hasLogin = true)
        {
            var person = new Person
            {
                Id = Guid.NewGuid(),
                FirstName = first,
                LastName = last,
                Roles = roles,
                DateOfBirth = dob,
                MembershipPlanId = planId,
                JoinedOn = new DateOnly(Math.Min(joinedYear, today.Year), 3, 15),
                UserId = hasLogin ? NewUser($"{first}.{last}".ToLowerInvariant()) : null,
            };
            db.Persons.Add(person);
            return person;
        }

        void Award(Person person, Rank rank, int stripes, DateOnly on, bool selfReported = false, Guid? by = null)
        {
            db.RankAwards.Add(new RankAward
            {
                Id = Guid.NewGuid(),
                PersonId = person.Id,
                RankId = rank.Id,
                Stripes = stripes,
                AwardedOn = on,
                SelfReported = selfReported,
                AwardedByPersonId = selfReported ? null : by ?? silva?.Id,
            });
        }

        ClassTemplate Template(string templateName, DayOfWeek day, TimeOnly start, int minutes, Guid? instructorId, params ClassType[] tags)
        {
            var template = new ClassTemplate
            {
                Id = Guid.NewGuid(),
                Name = templateName,
                Day = day,
                StartTime = start,
                DurationMinutes = minutes,
                DefaultInstructorPersonId = instructorId,
                ClassTypes = [.. tags],
            };
            db.ClassTemplates.Add(template);
            return template;
        }
    }
}
