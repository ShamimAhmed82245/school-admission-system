export type ApplicationStatus = "HELD" | "CONFIRMED" | "EXPIRED" | "CANCELLED";

export interface SchoolClass {
  id: string;
  name: string;
  totalSeats: number;
  heldSeats: number;
  confirmedSeats: number;
  availableSeats: number;
}

export interface Application {
  holdId: string;
  classId: string;
  studentId: string;
  status: ApplicationStatus;
  expiresAt: string;
}

export interface CreatedClass {
  id: string;
  name: string;
  totalSeats: number;
}

export interface HoldDetails {
  holdId: string;
  classId: string;
  status: ApplicationStatus;
  expiresAt: string;
}

export interface ConfirmationResult {
  holdId: string;
  status: ApplicationStatus;
}

export interface ApplicationSummary {
  holdId: string;
  classId: string;
  className: string;
  status: ApplicationStatus;
  expiresAt: string;
}

export interface ApplicationDetail extends HoldDetails {
  className: string;
}
