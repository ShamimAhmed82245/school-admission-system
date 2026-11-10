import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { admissionService as service } from "./services/admissionService";
import { errorMessage } from "./services/apiClient";
import type {
  ApplicationDetail,
  ApplicationStatus,
  ApplicationSummary,
  SchoolClass,
} from "./types";

type Screen = "classes" | "applications" | "create" | "application";
type Notice = { type: "success" | "error"; message: string };

const STUDENT_KEY = "school-admission:student-id";
const LEGACY_ACTIVE_HOLD_KEY = "school-admission:active-hold-id";
const ACTIVE_HOLD_KEY_PREFIX = "school-admission:active-hold-id:";
const STUDENT_ID_PATTERN = /^\d{17}$/;

function activeHoldKey(studentId: string) {
  return `${ACTIVE_HOLD_KEY_PREFIX}${studentId}`;
}

function getStoredActiveHold(studentId: string) {
  if (!STUDENT_ID_PATTERN.test(studentId)) return null;

  const studentHoldId = localStorage.getItem(activeHoldKey(studentId));
  if (studentHoldId) return studentHoldId;

  const legacyHoldId = localStorage.getItem(LEGACY_ACTIVE_HOLD_KEY);
  if (legacyHoldId) {
    localStorage.setItem(activeHoldKey(studentId), legacyHoldId);
    localStorage.removeItem(LEGACY_ACTIVE_HOLD_KEY);
  }
  return legacyHoldId;
}

function StatusBadge({ status }: { status: ApplicationStatus }) {
  return <span className={`status-badge status-${status.toLowerCase()}`}>{status}</span>;
}

function NoticeBanner({ notice }: { notice: Notice | null }) {
  if (!notice) return null;
  return (
    <div className={`notice notice-${notice.type}`} role="alert">
      <span className="notice-mark">{notice.type === "success" ? "✓" : "!"}</span>
      <span>{notice.message}</span>
    </div>
  );
}

function LoadingState() {
  return (
    <div className="state-box" aria-live="polite">
      <span className="spinner" />
      <p>Loading information...</p>
    </div>
  );
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat("en-GB", {
    day: "2-digit",
    month: "short",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  }).format(new Date(value));
}

function formatRemaining(milliseconds: number) {
  if (milliseconds <= 0) return "00:00";
  const totalSeconds = Math.floor(milliseconds / 1000);
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return `${String(minutes).padStart(2, "0")}:${String(seconds).padStart(2, "0")}`;
}

function ClassList({
  studentId,
  onOpenApplication,
}: {
  studentId: string;
  onOpenApplication: (holdId: string) => void;
}) {
  const [classes, setClasses] = useState<SchoolClass[]>([]);
  const [loading, setLoading] = useState(true);
  const [applyingId, setApplyingId] = useState<string | null>(null);
  const [notice, setNotice] = useState<Notice | null>(null);
  const loadingRef = useRef(false);

  const loadClasses = useCallback(async (background = false) => {
    if (loadingRef.current) return;
    loadingRef.current = true;
    if (!background) setLoading(true);
    try {
      setClasses(await service.getClasses());
      if (!background) setNotice(null);
    } catch (error) {
      setNotice({
        type: "error",
        message: errorMessage(
          error,
          "Unable to load classes. Please check the server connection and try again.",
        ),
      });
    } finally {
      loadingRef.current = false;
      if (!background) setLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadClasses();
    const interval = window.setInterval(() => void loadClasses(true), 5000);
    return () => window.clearInterval(interval);
  }, [loadClasses]);

  async function handleApply(classItem: SchoolClass) {
    setNotice(null);
    if (!STUDENT_ID_PATTERN.test(studentId)) {
      setNotice({
        type: "error",
        message: "Enter a valid 17-digit Student Birth Registration Number before applying.",
      });
      return;
    }
    if (classItem.availableSeats <= 0) {
      setNotice({ type: "error", message: "No seats are currently available for this class." });
      return;
    }
    setApplyingId(classItem.id);
    try {
      const application = await service.createHold(classItem.id, studentId);
      onOpenApplication(application.holdId);
    } catch (error) {
      setNotice({
        type: "error",
        message: error instanceof Error ? error.message : "Could not create the application.",
      });
      void loadClasses(true);
    } finally {
      setApplyingId(null);
    }
  }

  return (
    <section>
      <div className="page-heading">
        <div>
          <p className="eyebrow">Admissions</p>
          <h1>Available Classes</h1>
          <p>Review current seat availability and apply for a class.</p>
        </div>
        <div className="live-indicator">
          <span />
          Live seat updates
        </div>
      </div>
      <NoticeBanner notice={notice} />
      {notice?.type === "error" && classes.length === 0 && !loading && (
        <button className="button button-primary retry-button" onClick={() => void loadClasses()}>
          Retry
        </button>
      )}
      {loading ? (
        <LoadingState />
      ) : classes.length === 0 ? (
        <div className="state-box">
          <h2>No classes available</h2>
          <p>Newly created classes will appear here.</p>
        </div>
      ) : (
        <div className="table-shell">
          <div className="table-scroll">
            <table>
              <thead>
                <tr>
                  <th>Class Name</th>
                  <th>Total Seats</th>
                  <th>Held Seats</th>
                  <th>Confirmed Seats</th>
                  <th>Available Seats</th>
                  <th>
                    <span className="sr-only">Action</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {classes.map((classItem) => (
                  <tr key={classItem.id}>
                    <td data-label="Class Name" className="class-name">
                      {classItem.name}
                    </td>
                    <td data-label="Total Seats">{classItem.totalSeats}</td>
                    <td data-label="Held Seats">{classItem.heldSeats}</td>
                    <td data-label="Confirmed Seats">{classItem.confirmedSeats}</td>
                    <td data-label="Available Seats">
                      <span
                        className={
                          classItem.availableSeats > 0 ? "seat-available" : "seat-unavailable"
                        }
                      >
                        {classItem.availableSeats}
                      </span>
                    </td>
                    <td className="action-cell">
                      <button
                        className="button button-primary button-small"
                        disabled={applyingId === classItem.id || classItem.availableSeats === 0}
                        onClick={() => void handleApply(classItem)}
                      >
                        {applyingId === classItem.id ? "Applying..." : "Apply"}
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </section>
  );
}

function CreateClass({ onCreated }: { onCreated: () => void }) {
  const [name, setName] = useState("");
  const [seats, setSeats] = useState("");
  const [errors, setErrors] = useState<{ name?: string; seats?: string }>({});
  const [notice, setNotice] = useState<Notice | null>(null);
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault();
    const nextErrors: { name?: string; seats?: string } = {};
    const seatCount = Number(seats);
    if (!name.trim()) nextErrors.name = "Class name is required.";
    if (!seats) nextErrors.seats = "Total seats is required.";
    else if (!Number.isInteger(seatCount) || seatCount <= 0)
      nextErrors.seats = "Enter a positive whole number.";

    setErrors(nextErrors);
    setNotice(null);
    if (Object.keys(nextErrors).length > 0) return;

    setSubmitting(true);
    try {
      await service.createClass({ name: name.trim(), totalSeats: seatCount });
      setName("");
      setSeats("");
      setNotice({ type: "success", message: "Class created successfully." });
      onCreated();
    } catch (error) {
      setNotice({
        type: "error",
        message: errorMessage(
          error,
          "Unable to create the class. Please check the server connection and try again.",
        ),
      });
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <section>
      <div className="page-heading">
        <div>
          <p className="eyebrow">School administration</p>
          <h1>Create Class</h1>
          <p>Add a class or shift and define its total seat capacity.</p>
        </div>
      </div>
      <div className="form-card">
        <div className="form-card-header">
          <div className="section-icon" aria-hidden="true">
            +
          </div>
          <div>
            <h2>Class details</h2>
            <p>Each shift must be created as a separate class.</p>
          </div>
        </div>
        <NoticeBanner notice={notice} />
        <form onSubmit={(event) => void handleSubmit(event)} noValidate>
          <div className="field">
            <label htmlFor="class-name">Class Name</label>
            <input
              id="class-name"
              value={name}
              onChange={(event) => {
                setName(event.target.value);
                if (errors.name) setErrors((current) => ({ ...current, name: undefined }));
              }}
              placeholder="e.g. Class 6 - Morning Shift"
              aria-invalid={Boolean(errors.name)}
              aria-describedby={errors.name ? "class-name-error" : undefined}
            />
            {errors.name && (
              <p className="field-error" id="class-name-error">
                {errors.name}
              </p>
            )}
          </div>
          <div className="field">
            <label htmlFor="total-seats">Total Seats</label>
            <input
              id="total-seats"
              type="number"
              min="1"
              step="1"
              value={seats}
              onChange={(event) => {
                setSeats(event.target.value);
                if (errors.seats) setErrors((current) => ({ ...current, seats: undefined }));
              }}
              placeholder="e.g. 120"
              aria-invalid={Boolean(errors.seats)}
              aria-describedby={errors.seats ? "total-seats-error" : "seat-help"}
            />
            {errors.seats ? (
              <p className="field-error" id="total-seats-error">
                {errors.seats}
              </p>
            ) : (
              <p className="field-help" id="seat-help">
                Enter the maximum number of students for this class.
              </p>
            )}
          </div>
          <div className="form-actions">
            <button className="button button-primary" disabled={submitting} type="submit">
              {submitting ? "Creating..." : "Create Class"}
            </button>
          </div>
        </form>
      </div>
    </section>
  );
}

function MyApplications({
  studentId,
  onSelect,
  initialNotice,
}: {
  studentId: string;
  onSelect: (holdId: string, isActive: boolean) => void;
  initialNotice: Notice | null;
}) {
  const [applications, setApplications] = useState<ApplicationSummary[]>([]);
  const [loading, setLoading] = useState(false);
  const [notice, setNotice] = useState<Notice | null>(initialNotice);
  const [now, setNow] = useState(Date.now());
  const loadingRef = useRef(false);
  const validStudentId = STUDENT_ID_PATTERN.test(studentId);

  const loadApplications = useCallback(
    async (background = false) => {
      if (loadingRef.current) return;
      if (!validStudentId) {
        setApplications([]);
        return;
      }
      loadingRef.current = true;
      if (!background) setLoading(true);
      try {
        setApplications(await service.getMyHolds(studentId));
        setNotice(initialNotice);
      } catch (error) {
        setNotice({
          type: "error",
          message: errorMessage(
            error,
            "Unable to load applications. Please check the server connection and try again.",
          ),
        });
      } finally {
        loadingRef.current = false;
        if (!background) setLoading(false);
      }
    },
    [initialNotice, studentId, validStudentId],
  );

  useEffect(() => {
    void loadApplications();
    if (!validStudentId) return;
    const poll = window.setInterval(() => void loadApplications(true), 5000);
    return () => window.clearInterval(poll);
  }, [loadApplications, validStudentId]);

  useEffect(() => {
    if (!applications.some((application) => application.status === "HELD")) return;
    const timer = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, [applications]);

  return (
    <section>
      <div className="page-heading">
        <div>
          <p className="eyebrow">Student applications</p>
          <h1>My Applications</h1>
          <p>View all applications linked to the student number above.</p>
        </div>
      </div>
      <NoticeBanner notice={notice} />
      {notice?.type === "error" && applications.length === 0 && !loading && (
        <button
          className="button button-primary retry-button"
          onClick={() => void loadApplications()}
        >
          Retry
        </button>
      )}
      {!validStudentId ? (
        <div className="state-box">
          <div className="empty-icon" aria-hidden="true">
            #
          </div>
          <h2>Enter a valid student number</h2>
          <p>A 17-digit Student Birth Registration Number is required to view applications.</p>
        </div>
      ) : loading ? (
        <LoadingState />
      ) : applications.length === 0 ? (
        <div className="state-box">
          <div className="empty-icon" aria-hidden="true">
            ≡
          </div>
          <h2>No applications yet</h2>
          <p>Applications made from the Class List will appear here.</p>
        </div>
      ) : (
        <div className="table-shell">
          <div className="table-scroll">
            <table>
              <thead>
                <tr>
                  <th>Class Name</th>
                  <th>Application / Hold ID</th>
                  <th>Status</th>
                  <th>Expiration / Remaining Time</th>
                  <th>
                    <span className="sr-only">Open</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {applications.map((application) => (
                  <tr
                    className="clickable-row"
                    key={application.holdId}
                    onClick={() => onSelect(application.holdId, application.status === "HELD")}
                  >
                    <td data-label="Class Name" className="class-name">
                      {application.className}
                    </td>
                    <td data-label="Application / Hold ID" className="mono">
                      {application.holdId}
                    </td>
                    <td data-label="Status">
                      <StatusBadge status={application.status} />
                    </td>
                    <td data-label="Expiration / Remaining Time">
                      <span className="history-date">{formatDate(application.expiresAt)}</span>
                      <small>
                        {application.status === "HELD"
                          ? `${formatRemaining(new Date(application.expiresAt).getTime() - now)} remaining`
                          : "Hold expiration time"}
                      </small>
                    </td>
                    <td className="row-arrow" aria-label="Open application">
                      ›
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </section>
  );
}

function ApplicationScreen({
  holdId,
  studentId,
  isActiveHold,
  onActiveFound,
  onComplete,
  onReturnToClasses,
}: {
  holdId: string | null;
  studentId: string;
  isActiveHold: boolean;
  onActiveFound: (holdId: string) => void;
  onComplete: (notice?: Notice) => void;
  onReturnToClasses: () => void;
}) {
  const [application, setApplication] = useState<ApplicationDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [action, setAction] = useState<"confirm" | "cancel" | null>(null);
  const [notice, setNotice] = useState<Notice | null>(null);
  const [now, setNow] = useState(Date.now());
  const loadingRef = useRef(false);

  const loadApplication = useCallback(
    async (background = false) => {
      if (loadingRef.current) return;
      loadingRef.current = true;
      if (!background) {
        setLoading(true);
        setApplication(null);
        setNotice(null);
      }
      if (!holdId) {
        if (!STUDENT_ID_PATTERN.test(studentId)) {
          loadingRef.current = false;
          setLoading(false);
          return;
        }
        try {
          const applications = await service.getMyHolds(studentId);
          const activeApplication = applications.find(
            (application) => application.status === "HELD",
          );
          if (activeApplication) onActiveFound(activeApplication.holdId);
        } catch (error) {
          setNotice({
            type: "error",
            message: errorMessage(error, "Unable to find the current application."),
          });
        } finally {
          loadingRef.current = false;
          setLoading(false);
        }
        return;
      }
      try {
        const current = await service.getHold(holdId, studentId);
        setApplication(current);
        setNotice(null);
        if (isActiveHold && current.status !== "HELD") onComplete();
      } catch (error) {
        setNotice({
          type: "error",
          message: errorMessage(error, "Unable to load the application."),
        });
      } finally {
        loadingRef.current = false;
        if (!background) setLoading(false);
      }
    },
    [holdId, isActiveHold, onActiveFound, onComplete, studentId],
  );

  useEffect(() => {
    void loadApplication();
  }, [loadApplication]);

  useEffect(() => {
    if (application?.status !== "HELD") return;
    const timer = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, [application?.status]);

  useEffect(() => {
    if (!holdId) return;
    const poll = window.setInterval(() => void loadApplication(true), 5000);
    return () => window.clearInterval(poll);
  }, [holdId, loadApplication]);

  useEffect(() => {
    if (application?.status !== "HELD") return;
    if (new Date(application.expiresAt).getTime() > now) return;
    void loadApplication(true);
  }, [application?.expiresAt, application?.status, loadApplication, now]);

  const remaining = useMemo(
    () => (application ? new Date(application.expiresAt).getTime() - now : 0),
    [application, now],
  );

  async function updateApplication(type: "confirm" | "cancel") {
    if (!application) return;
    setAction(type);
    setNotice(null);
    try {
      if (type === "confirm") {
        await service.confirmHold(application.holdId, studentId);
        onComplete({ type: "success", message: "Admission fee paid. Admission is confirmed." });
      } else {
        await service.cancelHold(application.holdId, studentId);
        onComplete({ type: "success", message: "Application cancelled successfully." });
      }
    } catch (error) {
      setNotice({
        type: "error",
        message: errorMessage(error, "Unable to update the application."),
      });
      await loadApplication();
    } finally {
      setAction(null);
    }
  }

  return (
    <section>
      <div className="page-heading status-heading">
        <div>
          <p className="eyebrow">Active seat hold</p>
          <h1>Application</h1>
          <p>Complete or cancel this active admission application.</p>
        </div>
      </div>
      <NoticeBanner notice={notice} />
      {notice?.type === "error" &&
        !application &&
        !loading &&
        (holdId || STUDENT_ID_PATTERN.test(studentId)) && (
          <button
            className="button button-primary retry-button"
            onClick={() => void loadApplication()}
          >
            Retry
          </button>
        )}
      {loading ? (
        <LoadingState />
      ) : application ? (
        <div className="status-card">
          <div className="status-card-top">
            <div>
              <p className="detail-label">Class Name</p>
              <h2>{application.className}</h2>
              <p className="hold-id">{application.holdId}</p>
            </div>
            <StatusBadge status={application.status} />
          </div>
          <div className="detail-grid">
            <div>
              <p className="detail-label">Application / Hold ID</p>
              <p className="detail-value mono">{application.holdId}</p>
            </div>
            <div>
              <p className="detail-label">Application Status</p>
              <p className="detail-value">{application.status}</p>
            </div>
            <div>
              <p className="detail-label">Expiration Time</p>
              <p className="detail-value">{formatDate(application.expiresAt)}</p>
            </div>
            <div>
              <p className="detail-label">Remaining Time</p>
              <p
                className={`countdown ${application.status !== "HELD" ? "countdown-inactive" : ""}`}
              >
                {application.status === "HELD" ? formatRemaining(remaining) : "—"}
              </p>
            </div>
          </div>
          {application.status === "HELD" ? (
            <div className="status-actions">
              <div className="hold-note">
                <span aria-hidden="true">i</span>
                Your seat is temporarily held. Complete the simulated payment before time expires.
              </div>
              <div className="button-row">
                <button
                  className="button button-primary"
                  disabled={action !== null || remaining <= 0}
                  onClick={() => void updateApplication("confirm")}
                >
                  {action === "confirm" ? "Confirming..." : "ভর্তি ফি দিন / Pay Admission Fee"}
                </button>
                <button
                  className="button button-danger"
                  disabled={action !== null || remaining <= 0}
                  onClick={() => void updateApplication("cancel")}
                >
                  {action === "cancel" ? "Cancelling..." : "Cancel Application"}
                </button>
              </div>
            </div>
          ) : (
            <div className={`terminal-message terminal-${application.status.toLowerCase()}`}>
              {application.status === "CONFIRMED" &&
                "Admission is confirmed. No further payment action is required."}
              {application.status === "EXPIRED" &&
                "This seat hold has expired. Payment and cancellation are no longer available."}
              {application.status === "CANCELLED" &&
                "This application was cancelled and is no longer active."}
            </div>
          )}
        </div>
      ) : (
        <div className="state-box">
          <div className="empty-icon" aria-hidden="true">
            ≡
          </div>
          <h2>No active application</h2>
          <p>Apply for an available class to create a new seat hold.</p>
          <button className="button button-primary empty-action" onClick={onReturnToClasses}>
            Return to Class List
          </button>
        </div>
      )}
    </section>
  );
}

export default function App() {
  const [studentId, setStudentId] = useState(() => localStorage.getItem(STUDENT_KEY) ?? "");
  const [selectedHoldId, setSelectedHoldId] = useState<string | null>(() =>
    getStoredActiveHold(localStorage.getItem(STUDENT_KEY) ?? ""),
  );
  const [screen, setScreen] = useState<Screen>(() =>
    getStoredActiveHold(localStorage.getItem(STUDENT_KEY) ?? "") ? "application" : "classes",
  );
  const [studentTouched, setStudentTouched] = useState(false);
  const [classVersion, setClassVersion] = useState(0);
  const [applicationNotice, setApplicationNotice] = useState<Notice | null>(null);

  function updateStudentId(value: string) {
    const sanitized = value.replace(/\D/g, "").slice(0, 17);
    setStudentId(sanitized);
    localStorage.setItem(STUDENT_KEY, sanitized);
    setSelectedHoldId(getStoredActiveHold(sanitized));
  }

  function navigate(nextScreen: Screen) {
    if (nextScreen === "application") {
      setSelectedHoldId(getStoredActiveHold(studentId));
    }
    if (nextScreen !== "applications") setApplicationNotice(null);
    setScreen(nextScreen);
    window.scrollTo({ top: 0, behavior: "smooth" });
  }

  function openApplication(holdId: string, isActive = true) {
    setApplicationNotice(null);
    setSelectedHoldId(holdId);
    if (isActive) localStorage.setItem(activeHoldKey(studentId), holdId);
    setScreen("application");
    window.scrollTo({ top: 0, behavior: "smooth" });
  }

  const completeApplication = useCallback(
    (notice?: Notice) => {
      localStorage.removeItem(activeHoldKey(studentId));
      setSelectedHoldId(null);
      setApplicationNotice(notice ?? null);
      setScreen("applications");
      window.scrollTo({ top: 0, behavior: "smooth" });
    },
    [studentId],
  );

  const studentError =
    studentTouched && studentId.length > 0 && !STUDENT_ID_PATTERN.test(studentId);

  return (
    <div className="app-shell">
      <header className="site-header">
        <div className="header-inner">
          <div className="brand" onClick={() => navigate("classes")} role="button" tabIndex={0}>
            <div className="brand-mark" aria-hidden="true">
              SA
            </div>
            <div>
              <strong>School Admission System</strong>
              <span>Simple, fair and transparent admissions</span>
            </div>
          </div>
          <div className="student-id-field">
            <label htmlFor="student-id">Student Birth Registration Number</label>
            <div className={`student-input-wrap ${studentError ? "input-error" : ""}`}>
              <span aria-hidden="true">#</span>
              <input
                id="student-id"
                inputMode="numeric"
                autoComplete="off"
                placeholder="Enter 17-digit number"
                value={studentId}
                onChange={(event) => updateStudentId(event.target.value)}
                onBlur={() => setStudentTouched(true)}
                aria-invalid={studentError}
              />
              <small>{studentId.length}/17</small>
            </div>
            {studentError && <p>Student number must contain exactly 17 digits.</p>}
          </div>
        </div>
        <nav aria-label="Main navigation">
          <div className="nav-inner">
            <button
              className={screen === "classes" ? "active" : ""}
              onClick={() => navigate("classes")}
            >
              Class List
            </button>
            <button
              className={screen === "application" ? "active" : ""}
              onClick={() => navigate("application")}
            >
              Application
            </button>
            <button
              className={screen === "applications" ? "active" : ""}
              onClick={() => navigate("applications")}
            >
              My Applications
            </button>
            <button
              className={screen === "create" ? "active" : ""}
              onClick={() => navigate("create")}
            >
              Create Class
            </button>
          </div>
        </nav>
      </header>
      <main>
        {screen === "classes" && (
          <ClassList key={classVersion} studentId={studentId} onOpenApplication={openApplication} />
        )}
        {screen === "applications" && (
          <MyApplications
            key={studentId}
            studentId={studentId}
            onSelect={openApplication}
            initialNotice={applicationNotice}
          />
        )}
        {screen === "create" && (
          <CreateClass onCreated={() => setClassVersion((version) => version + 1)} />
        )}
        {screen === "application" && (
          <ApplicationScreen
            key={`${studentId}:${selectedHoldId ?? "none"}`}
            holdId={selectedHoldId}
            studentId={studentId}
            isActiveHold={selectedHoldId === getStoredActiveHold(studentId)}
            onActiveFound={(holdId) => openApplication(holdId)}
            onComplete={completeApplication}
            onReturnToClasses={() => navigate("classes")}
          />
        )}
      </main>
      <footer>
        <p>School Admission System</p>
        <span>Simple and transparent admission application processing.</span>
      </footer>
    </div>
  );
}
