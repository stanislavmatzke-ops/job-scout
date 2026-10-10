import { EmploymentType, RemoteWork } from './matches.models';

export type Seniority = 'Junior' | 'Medior' | 'Senior' | 'Lead' | 'Expert';
export type GrammaticalGender = 'Unspecified' | 'Masculine' | 'Feminine';

/** What the AI made of the CV and the notes – the basis of every search and evaluation. */
export interface CandidateProfile {
  headline: string;
  yearsOfExperience: number;
  seniority: Seniority;
  targetRoles: string[];
  coreSkills: string[];
  otherSkills: string[];
  domains: string[];
  languages: string[];
  preferences: {
    remoteWork: RemoteWork;
    acceptableRemoteWork: RemoteWork[];
    employmentTypes: EmploymentType[];
    minMonthlySalaryCzk: number;
    priorities: string[];
    avoid: string[];
  };
  search: {
    professions: string[];
    keywords: string[];
    homeLocation: string;
    commuteRadiusKm: number;
    extraLocations: string[];
  };
}

export interface ProfileView {
  version: number | null;
  cvFileName: string | null;
  notes: string | null;
  analyzedAt: string | null;
  profile: CandidateProfile | null;
  gender: GrammaticalGender;
  hasCv: boolean;
  /** Jobs waiting for a decision that were evaluated with an older profile version. */
  outdatedEvaluations: number;
}
