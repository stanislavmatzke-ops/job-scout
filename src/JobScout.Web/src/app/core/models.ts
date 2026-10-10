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
}

export interface AuthProviders {
  external: string[];
  devSignIn: boolean;
}
