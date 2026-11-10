import type {
  Application,
  ApplicationSummary,
  ConfirmationResult,
  CreatedClass,
  HoldDetails,
  SchoolClass,
} from "../types";
import type { AdmissionService } from "./admissionService";
import { apiRequest } from "./apiClient";

const encodeId = (value: string) => encodeURIComponent(value);

export const backendAdmissionService: AdmissionService = {
  createClass(input) {
    return apiRequest<CreatedClass>("/classes", {
      method: "POST",
      body: input,
    });
  },

  getClasses() {
    return apiRequest<SchoolClass[]>("/classes");
  },

  getClass(classId) {
    return apiRequest<SchoolClass>(`/classes/${encodeId(classId)}`);
  },

  createHold(classId, studentId) {
    return apiRequest<Application>(`/classes/${encodeId(classId)}/holds`, {
      method: "POST",
      studentId,
    });
  },

  async getHold(holdId, _studentId) {
    const hold = await apiRequest<HoldDetails>(`/holds/${encodeId(holdId)}`);
    const classItem = await apiRequest<SchoolClass>(`/classes/${encodeId(hold.classId)}`);
    return { ...hold, className: classItem.name };
  },

  confirmHold(holdId, studentId) {
    return apiRequest<ConfirmationResult>(`/holds/${encodeId(holdId)}/confirm`, {
      method: "POST",
      studentId,
    });
  },

  cancelHold(holdId, studentId) {
    return apiRequest<void>(`/holds/${encodeId(holdId)}`, {
      method: "DELETE",
      studentId,
    });
  },

  getMyHolds(studentId) {
    return apiRequest<ApplicationSummary[]>("/me/holds", { studentId });
  },
};
