import type { Application, ApplicationDetail, ApplicationSummary, SchoolClass } from "../types";
import type { AdmissionService } from "./admissionService";

const CLASSES_KEY = "school-admission:classes";
const APPLICATIONS_KEY = "school-admission:applications";
const HOLD_DURATION_MS = 10 * 60 * 1000;

const initialClasses: SchoolClass[] = [
  {
    id: "class-6-morning",
    name: "Class 6 - Morning Shift",
    totalSeats: 120,
    heldSeats: 15,
    confirmedSeats: 80,
    availableSeats: 25,
  },
  {
    id: "class-6-day",
    name: "Class 6 - Day Shift",
    totalSeats: 100,
    heldSeats: 8,
    confirmedSeats: 64,
    availableSeats: 28,
  },
  {
    id: "class-7-morning",
    name: "Class 7 - Morning Shift",
    totalSeats: 90,
    heldSeats: 4,
    confirmedSeats: 72,
    availableSeats: 14,
  },
];

const pause = () => new Promise((resolve) => window.setTimeout(resolve, 180));

function readClasses(): SchoolClass[] {
  const stored = localStorage.getItem(CLASSES_KEY);
  if (stored) return JSON.parse(stored) as SchoolClass[];
  localStorage.setItem(CLASSES_KEY, JSON.stringify(initialClasses));
  return initialClasses;
}

function readApplications(): Application[] {
  const stored = localStorage.getItem(APPLICATIONS_KEY);
  return stored ? (JSON.parse(stored) as Application[]) : [];
}

function writeClasses(classes: SchoolClass[]) {
  localStorage.setItem(CLASSES_KEY, JSON.stringify(classes));
}

function writeApplications(applications: Application[]) {
  localStorage.setItem(APPLICATIONS_KEY, JSON.stringify(applications));
}

function syncExpirations() {
  const applications = readApplications();
  const classes = readClasses();
  let changed = false;

  applications.forEach((application) => {
    if (application.status === "HELD" && new Date(application.expiresAt).getTime() <= Date.now()) {
      application.status = "EXPIRED";
      const classItem = classes.find((item) => item.id === application.classId);
      if (classItem) {
        classItem.heldSeats = Math.max(0, classItem.heldSeats - 1);
        classItem.availableSeats += 1;
      }
      changed = true;
    }
  });

  if (changed) {
    writeApplications(applications);
    writeClasses(classes);
  }
}

function findOwnedApplication(holdId: string, studentId: string) {
  syncExpirations();
  const application = readApplications().find(
    (item) => item.holdId === holdId && item.studentId === studentId,
  );
  if (!application) throw new Error("Application not found for this student.");
  return application;
}

export const mockAdmissionService: AdmissionService = {
  async createClass({ name, totalSeats }) {
    await pause();
    const classes = readClasses();
    const newClass: SchoolClass = {
      id: `class-${crypto.randomUUID()}`,
      name,
      totalSeats,
      heldSeats: 0,
      confirmedSeats: 0,
      availableSeats: totalSeats,
    };
    writeClasses([...classes, newClass]);
    return newClass;
  },

  async getClasses() {
    await pause();
    syncExpirations();
    return readClasses();
  },

  async getClass(classId) {
    await pause();
    syncExpirations();
    const classItem = readClasses().find((item) => item.id === classId);
    if (!classItem) throw new Error("Class not found.");
    return classItem;
  },

  async createHold(classId, studentId) {
    await pause();
    syncExpirations();
    const applications = readApplications();
    const duplicate = applications.some(
      (item) =>
        item.classId === classId &&
        item.studentId === studentId &&
        (item.status === "HELD" || item.status === "CONFIRMED"),
    );
    if (duplicate) {
      throw new Error("You already have an active or confirmed application for this class.");
    }

    const classes = readClasses();
    const classItem = classes.find((item) => item.id === classId);
    if (!classItem) throw new Error("Class not found.");
    if (classItem.availableSeats <= 0) {
      throw new Error("No seats are currently available for this class.");
    }

    const application: Application = {
      holdId: `HLD-${crypto.randomUUID().slice(0, 8).toUpperCase()}`,
      classId,
      studentId,
      status: "HELD",
      expiresAt: new Date(Date.now() + HOLD_DURATION_MS).toISOString(),
    };
    classItem.heldSeats += 1;
    classItem.availableSeats -= 1;
    writeApplications([...applications, application]);
    writeClasses(classes);
    return application;
  },

  async getHold(holdId, studentId) {
    await pause();
    const application = findOwnedApplication(holdId, studentId);
    const classItem = readClasses().find((item) => item.id === application.classId);
    if (!classItem) throw new Error("Class not found.");
    return { ...application, className: classItem.name };
  },

  async confirmHold(holdId, studentId) {
    await pause();
    const current = findOwnedApplication(holdId, studentId);
    if (current.status === "EXPIRED") {
      throw new Error("This application has expired and can no longer be confirmed.");
    }
    if (current.status === "CANCELLED") {
      throw new Error("This application was cancelled and can no longer be confirmed.");
    }
    if (current.status === "CONFIRMED") return current;

    const applications = readApplications();
    const application = applications.find((item) => item.holdId === holdId)!;
    const classes = readClasses();
    const classItem = classes.find((item) => item.id === application.classId);
    application.status = "CONFIRMED";
    if (classItem) {
      classItem.heldSeats = Math.max(0, classItem.heldSeats - 1);
      classItem.confirmedSeats += 1;
    }
    writeApplications(applications);
    writeClasses(classes);
    return application;
  },

  async cancelHold(holdId, studentId) {
    await pause();
    const current = findOwnedApplication(holdId, studentId);
    if (current.status !== "HELD") {
      throw new Error(`This application is already ${current.status.toLowerCase()}.`);
    }

    const applications = readApplications();
    const application = applications.find((item) => item.holdId === holdId)!;
    const classes = readClasses();
    const classItem = classes.find((item) => item.id === application.classId);
    application.status = "CANCELLED";
    if (classItem) {
      classItem.heldSeats = Math.max(0, classItem.heldSeats - 1);
      classItem.availableSeats += 1;
    }
    writeApplications(applications);
    writeClasses(classes);
  },

  async getMyHolds(studentId) {
    await pause();
    syncExpirations();
    const classes = readClasses();
    return readApplications()
      .filter((item) => item.studentId === studentId)
      .map<ApplicationSummary>((item) => ({
        holdId: item.holdId,
        classId: item.classId,
        className:
          classes.find((classItem) => classItem.id === item.classId)?.name ?? "Unknown class",
        status: item.status,
        expiresAt: item.expiresAt,
      }))
      .reverse();
  },
};
