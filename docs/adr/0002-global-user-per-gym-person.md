# Global User + per-gym Person with a role set

A human authenticates once platform-wide (User) and exists on each gym's roster as a Person carrying a *set* of roles {Owner, Admin, Instructor, Member} — never exclusive, so the coach who trains is one Person with one attendance history. Persons can exist with no User at all (children, members without devices), and a guardian's User manages linked child Persons. Users belonging to multiple gyms select the active gym at login; single-gym users skip the picker.

**Considered options**: per-gym accounts (rejected: a human at two gyms needs two logins, and cross-gym drop-in visits die permanently) and separate Instructor/Student entities (rejected: the dual-role coach becomes two records with duplicated bio and attendance — the exact inconsistency this model exists to prevent).
