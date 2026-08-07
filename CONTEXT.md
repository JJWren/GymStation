# GymStation

Multi-tenant management platform for combat-sports gyms (BJJ first): owner/admin dashboard plus member training portal. One shared vocabulary for code, DB, API, and default UI.

## Language

### Tenancy & people

**Gym**:
The tenant — one academy at one location. All tenant-owned data belongs to exactly one Gym.
_Avoid_: academy, school, club, location

**Gym Settings**:
A Gym's own configuration: swap-workflow mode, open-claims toggle, check-in window, class tags, expense categories, theme tokens, timezone.

**User**:
A global login identity — one per human, valid across all Gyms. A User in multiple Gyms picks the active Gym at login.
_Avoid_: account, login

**Person**:
A Gym's roster record for a human. May exist without a User (children, members without devices). A guardian's User manages linked child Persons.
_Avoid_: profile, contact

**Role**:
A per-Person set, never exclusive: Owner, Admin, Instructor, Member, Staff. A coach who trains holds {Instructor, Member} on one Person.

**Staff**:
A Person working for the Gym without portal privileges — front desk, cleaning. Owners, Admins, and Instructors are staff-ish by nature; the Staff role marks everyone else. Staff-ish Persons may carry a StaffProfile.
_Avoid_: employee, worker

**StaffProfile**:
Pay rate/unit plus bio, experience, and hobbies for any staff-ish Person. Pay is stored and displayed only — payroll computation stays deferred. Supersedes InstructorProfile.
_Avoid_: instructor profile (historical name)

**Family**:
A Gym's billing-and-guardianship group: member Persons plus guardian Users. The home of family plans, the primary payer, and acting-for-wards.
_Avoid_: household, group

**FamilyMember**:
A Person's membership in a Family. Wards (IsWard) are acted for by guardians; adult members are not.

**Ward**:
A FamilyMember flagged IsWard — guardians act on their behalf across the member portal, including their diary.
_Avoid_: child, minor (age is a fact; wardship is the modeled state)

**FamilyGuardian**:
A User's guardianship over a Family, carrying permission flags: act for wards (default on), manage guardians, manage members, view billing. Exactly one guardian is PRIMARY — all flags locked on, payments raised against their Person, transferable by themselves or a Gym admin.
_Avoid_: parent (guardians need not be parents)

**Graduation**:
The manual hand-off of a Ward to their own account: a login is invited, IsWard clears, and the entire diary — history included — becomes private to the new adult. Never automatic; the platform nudges the primary and admins when a Ward turns 18.
_Avoid_: emancipation, aging out

**Member**:
A Person with the Member role — someone who trains at the Gym.
_Avoid_: student, athlete, client

**Instructor**:
A Person with the Instructor role — someone who teaches ClassSessions. Instructors are the Gym's public faces: their portrait and profile appear on the Gym's public page.
_Avoid_: coach, professor, trainer (these may return later as per-Gym display labels)

### Scheduling

**ClassTemplate**:
A recurring weekly slot: day, time, duration, ClassTypes, default Instructor. Editing it changes the future pattern, never a single date. Effective from its start date — earlier weeks never materialize it.
_Avoid_: class (ambiguous), schedule entry

**ClassSession**:
A dated occurrence of a ClassTemplate (or a one-off). The unit of check-in, substitution, and cancellation.
_Avoid_: class, event, occurrence

**One-off**:
A ClassSession with no ClassTemplate — added directly or by duplication. It never follows a weekly pattern, and can be promoted to start one.
_Avoid_: extra class, ad-hoc session

**ClassType**:
A filterable tag on templates/sessions: gi, no-gi, kids, fundamentals, competition, open-mat, plus per-Gym custom tags.
_Avoid_: category; program (a Program is the public marketing offering — a ClassType is a schedule filter tag)

**Substitution**:
A temporary Instructor replacement on one ClassSession. Lifecycle: Requested (named or open) → Accepted/Claimed → PendingApproval (only in admin-gated Gyms) → Applied. Never mutates the ClassTemplate.
_Avoid_: swap (UI verb is fine; the record is a Substitution)

### Attendance

**Check-in**:
A Person's claim of presence at a ClassSession, made by the member, their guardian, or an Instructor. Creates a Pending AttendanceRecord.
_Avoid_: sign-in, booking, reservation (GymStation has no booking)

**AttendanceRecord**:
One Person × ClassSession with source (Self/Guardian/Instructor) and status: Pending → Confirmed (automatic at session end + 2h unless amended) or Removed.
_Avoid_: attendance entry, visit

### Ranks

**RankSystem**:
An ordered ladder of Ranks with stripe counts for one discipline. IBJJF adult and kids ladders ship seeded; Gyms can define custom systems. A Person holds at most one active Rank per RankSystem.
_Avoid_: belt system (a RankSystem may have no belts)

**Discipline**:
The Program a RankSystem is mapped to for a Gym — each Gym labels any ladder it can see (seeded or custom) with one of its Programs, and rank displays carry that label. Programs are the discipline list; an unmapped ladder falls back to its own name. The mapping is per-Gym because seeded ladders are shared.
_Avoid_: a separate Discipline entity (there isn't one — Programs are the disciplines)

**Rank**:
One rung of a RankSystem (e.g., Purple). Rank + stripe count locate a Person on the ladder.
_Avoid_: belt (the physical object; UI may say belt, the domain says Rank)

**RankAward**:
A dated promotion or stripe grant: who awarded it, when, optional note, self-reported flag (pre-app history). "Time at current rank" is always derived from RankAwards, never stored.
_Avoid_: promotion record (a stripe is not a promotion)

### Money

**MembershipPlan**:
A Gym's price + billing cadence, scoped per-person or per-family. A family-scoped plan is assigned to a Family and charged to its primary guardian's Person; covered members are skipped by the individual cycle. It may price by family composition: a base covering an included number of adults and wards, plus a per-head price for each extra in either lane — a comped plan is one whose computed total is zero.
_Avoid_: subscription, tier

**Covered**:
A FamilyMember whose dues ride their Family's active family-scoped plan — the individual cycle skips them while it stands. Any personal plan they hold lies dormant, never cleared, and resumes if the family plan goes away.
_Avoid_: exempt, included (included is plan sizing — how many the base price covers)

**Charge**:
An amount a Person owes the Gym, raised per plan cycle or ad hoc.
_Avoid_: invoice, bill

**Payment**:
A recorded settlement against a Person's Charges. v1 records money; it never moves money.
_Avoid_: transaction

**Balance**:
ΣCharges − ΣPayments for a Person. Always derived, never edited.
_Avoid_: amount owed (field), credit

**Expense**:
An owner-entered Gym outgoing (rent, insurance, equipment), categorized by per-Gym ExpenseCategories. Fully editable and deletable — bookkeeping, not an audit ledger.
_Avoid_: cost, bill

**OtherIncome**:
Owner-entered Gym revenue that isn't dues — seminars, merch, drop-in fees. Mirrors Expense (label/category, amount, received-on, note) and is equally editable and deletable. Net = dues collected + OtherIncome − Expenses.
_Avoid_: revenue (the derived total), earnings

### Member portal

**TrainingEntry**:
A Member's diary record — lesson notes, roll log (optionally tagging roster Persons), or self-reported training. Private to the member's account authority: the member themselves, or — for Wards — their acting guardians. Never visible to Instructors, Admins, or Owners. Graduation transfers the whole diary to the new adult alone.
_Avoid_: journal, log entry

**Mat Hours**:
Two-tier training time: gym-verified (derived from Confirmed AttendanceRecords; the only tier in owner statistics) and self-reported (from TrainingEntries; tagged in member views).
_Avoid_: training hours (ambiguous about tier)

**Event**:
An admin-published happening: tournament, seminar, grading. Members mark going/interested (visible within the Gym).
_Avoid_: class (Events are not ClassSessions)

### Public presence

**Program**:
A Gym's public marketing offering — BJJ, Bootcamp, Muay Thai — with title, description, and image, listed on the public page. What a prospective member joins for, not how the schedule is tagged.
_Avoid_: class type (a ClassType is a schedule filter tag), discipline, offering

**SuccessStory**:
A testimonial the Gym publishes on its public page: a story and who it's by.
_Avoid_: review, testimonial (the entity is a SuccessStory)

### Communication

**Notification**:
An outbox record fanned out through channel adapters (in-app, email; push later) per User preference. Categories include session cancelled/changed, substitution lifecycle, rank awarded, charge raised, admin escalations.
_Avoid_: alert, message (a ContactMessage is a visitor inquiry; broader member messaging stays deferred)

**ContactMessage**:
A visitor's inquiry submitted through the public page's contact form: name, a way to reach them (email or phone), and their message. Read by staff in the Gym's Messages box; never a Notification, though its arrival raises one.
_Avoid_: notification, lead, inquiry record
