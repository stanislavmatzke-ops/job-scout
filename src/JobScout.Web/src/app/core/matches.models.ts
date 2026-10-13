export type MatchView = 'Open' | 'InProgress' | 'Closed' | 'TriagedOut';
export type MatchStage = 'Found' | 'Shortlisted' | 'TriagedOut' | 'Evaluated';
export type MatchStatus =
  | 'New' | 'SavedForLater' | 'Applied' | 'Call' | 'Round1' | 'Round2' | 'Offer'
  | 'Rejected' | 'Withdrawn' | 'Dismissed' | 'Hidden';
export type RemoteWork = 'Unknown' | 'Full' | 'Hybrid' | 'Onsite';
export type EmploymentType = 'Unknown' | 'Employee' | 'Contractor' | 'Both';

export interface MatchCounts { open: number; inProgress: number; closed: number; triagedOut: number; }

export interface MatchListItem {
  id: string;
  title: string;
  company: string;
  location: string;
  url: string;
  stage: MatchStage;
  status: MatchStatus;
  score: number | null;
  triageScore: number | null;
  summary: string | null;
  triageReason: string | null;
  hasCoverLetter: boolean;
  foundAt: string;
  updatedAt: string;
  /** Evaluated with an older profile version than the current one (6d). */
  evaluationOutdated: boolean;
  /** New AI drafts over an existing letter – limited per job (ADR-0021). */
  letterRewrites?: number;
}

export interface MatchList { view: MatchView; items: MatchListItem[]; counts: MatchCounts; }

export interface JobEvaluation {
  score: number;
  summary: string;
  reasons: string[];
  risks: string[];
  questions: string[];
  salary: string;
  remoteWork: RemoteWork;
  employmentType: EmploymentType;
}

export interface MatchHistoryEntry { from: MatchStatus; to: MatchStatus; note: string | null; at: string; }

export interface MatchDetail {
  id: string;
  title: string;
  company: string;
  location: string;
  url: string;
  source: string;
  stage: MatchStage;
  status: MatchStatus;
  score: number | null;
  triageScore: number | null;
  triageReason: string | null;
  evaluation: JobEvaluation | null;
  coverLetter: string | null;
  notes: string | null;
  adText: string | null;
  adTextIncomplete: boolean;
  foundAt: string;
  updatedAt: string;
  history: MatchHistoryEntry[];
  /** Evaluated with an older profile version than the current one (6d). */
  evaluationOutdated: boolean;
  /** New AI drafts over an existing letter – limited per job (ADR-0021). */
  letterRewrites?: number;
}
