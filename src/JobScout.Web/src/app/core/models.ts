export type AccessStatus = 'Pending' | 'Approved' | 'Suspended';

export interface Me {
  id: string;
  provider: string;
  displayName: string;
  email: string;
  access: AccessStatus;
  accessRequestedAt: string | null;
  suspendedReason: string | null;
  isAdmin: boolean;
  identity: string;
  rejectedAt?: string | null;        // the last request was turned down (stays Pending)
  rejectedReason?: string | null;
  isDemo?: boolean;                  // the shared demo account – no AI, no CV/letter changes (ADR-0020)
}

export interface AuthProviders {
  external: string[];
  devSignIn: boolean;
  demo?: string | null;              // display name of the demo account, null = no "try it" button
}
