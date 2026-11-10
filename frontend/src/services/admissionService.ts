import type {
  Application,
  ApplicationDetail,
  ApplicationSummary,
  ConfirmationResult,
  CreatedClass,
  SchoolClass,
} from "../types";
import { backendAdmissionService } from "./backendAdmissionService";
import { mockAdmissionService } from "./mockAdmissionService";
export { API_BASE_URL, studentHeader } from "./apiClient";

export const USE_BACKEND_API = import.meta.env.VITE_USE_BACKEND_API === "true";

export const apiContract = {
  createClass: { method: "POST", path: "/classes" },
  getClasses: { method: "GET", path: "/classes" },
  getClass: { method: "GET", path: "/classes/{classId}" },
  createHold: { method: "POST", path: "/classes/{classId}/holds" },
  getHold: { method: "GET", path: "/holds/{holdId}" },
  confirmHold: { method: "POST", path: "/holds/{holdId}/confirm" },
  cancelHold: { method: "DELETE", path: "/holds/{holdId}" },
  getMyHolds: { method: "GET", path: "/me/holds" },
} as const;

export interface AdmissionService {
  createClass(input: { name: string; totalSeats: number }): Promise<CreatedClass>;
  getClasses(): Promise<SchoolClass[]>;
  getClass(classId: string): Promise<SchoolClass>;
  createHold(classId: string, studentId: string): Promise<Application>;
  getHold(holdId: string, studentId: string): Promise<ApplicationDetail>;
  confirmHold(holdId: string, studentId: string): Promise<ConfirmationResult>;
  cancelHold(holdId: string, studentId: string): Promise<void>;
  getMyHolds(studentId: string): Promise<ApplicationSummary[]>;
}

export const admissionService: AdmissionService = USE_BACKEND_API
  ? backendAdmissionService
  : mockAdmissionService;
