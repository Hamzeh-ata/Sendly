import { ProjectListRow } from './project-row.model';

export interface ProjectCountryGroup {
  countryKey: string;
  countryLabel: string;
  projects: ProjectListRow[];
}
