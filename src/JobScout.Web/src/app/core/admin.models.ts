import { AccessStatus } from './models';

/** One row of GET /api/admin/users – sorted by the API: waiting requests first, then the most expensive. */
export interface AdminUser {
  id: string;
  provider: string;
  displayName: string;
  email: string;
  access: AccessStatus;
  createdAt: string;
  lastSignInAt: string | null;
  accessRequestedAt: string | null;
  accessRequestMessage: string | null;
  suspendedReason: string | null;
  hasProfile: boolean;
  costLast7DaysUsd: number;
  costLast30DaysUsd: number;
}

export interface Invitation {
  id: string;
  note: string;
  createdAt: string;
  expiresAt: string;
  usedAt: string | null;
  usedBy: string | null;
  isUsable: boolean;
}

/** The answer of POST /api/admin/invitations – the only time the code is ever seen. */
export interface CreatedInvitation {
  id: string;
  code: string;
  expiresAt: string;
}

export interface InvitationLink {
  provider: string;
  label: string;
  url: string;
}
