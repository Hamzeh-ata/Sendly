import { MapCascadeOption, MapProjectOption } from '../models/map-research.model';

export function mapCascadeProject(
  id: string,
  name: string,
  countries: string[] | null | undefined,
  cities: string[] | null | undefined
): MapProjectOption {
  const family = resolveProjectFamily(name);
  return {
    id,
    name,
    familyKey: family.key,
    familyLabel: family.label,
    country: firstNonEmpty(countries),
    city: firstNonEmpty(cities)
  };
}

export function resolveProjectFamily(name: string): { key: string; label: string } {
  const trimmed = name.trim();
  const splitIndex = findFamilySplitIndex(trimmed);
  if (splitIndex <= 0) {
    return { key: trimmed.toLowerCase(), label: trimmed || '—' };
  }
  const label = trimmed.slice(0, splitIndex).trim();
  return { key: label.toLowerCase(), label: label || trimmed };
}

export function listFamilyOptions(projects: MapProjectOption[]): MapCascadeOption[] {
  const options: MapCascadeOption[] = [];
  const seen = new Set<string>();
  for (const project of projects) {
    if (seen.has(project.familyKey)) {
      continue;
    }
    seen.add(project.familyKey);
    options.push({ value: project.familyKey, label: project.familyLabel });
  }
  options.sort((a, b) => a.label.localeCompare(b.label));
  return options;
}

export function listCountryOptions(
  projects: MapProjectOption[],
  familyKey: string
): MapCascadeOption[] {
  const options: MapCascadeOption[] = [];
  const seen = new Set<string>();
  if (!familyKey) {
    return options;
  }
  for (const project of projects) {
    if (project.familyKey !== familyKey || !project.country || seen.has(project.country)) {
      continue;
    }
    seen.add(project.country);
    options.push({ value: project.country, label: project.country });
  }
  options.sort((a, b) => a.label.localeCompare(b.label));
  return options;
}

export function listCityOptions(
  projects: MapProjectOption[],
  familyKey: string,
  country: string
): MapCascadeOption[] {
  const options: MapCascadeOption[] = [];
  const seen = new Set<string>();
  if (!familyKey || !country) {
    return options;
  }
  for (const project of projects) {
    if (
      project.familyKey !== familyKey
      || project.country !== country
      || !project.city
      || seen.has(project.city)
    ) {
      continue;
    }
    seen.add(project.city);
    options.push({ value: project.city, label: project.city });
  }
  options.sort((a, b) => a.label.localeCompare(b.label));
  return options;
}

export function hasCountryWideProject(
  projects: MapProjectOption[],
  familyKey: string,
  country: string
): boolean {
  if (!familyKey || !country) {
    return false;
  }
  for (const project of projects) {
    if (project.familyKey === familyKey && project.country === country && !project.city) {
      return true;
    }
  }
  return false;
}

export function resolveMatchingProjectIds(
  projects: MapProjectOption[],
  familyKey: string,
  country: string,
  city: string
): string[] {
  if (!familyKey) {
    return [];
  }

  const ids: string[] = [];
  for (const project of projects) {
    if (project.familyKey !== familyKey) {
      continue;
    }
    if (country && project.country !== country) {
      continue;
    }
    if (city && project.city !== city) {
      continue;
    }
    ids.push(project.id);
  }
  return ids;
}

export function findCascadeProject(
  projects: MapProjectOption[],
  projectId: string
): MapProjectOption | null {
  if (!projectId) {
    return null;
  }
  for (const project of projects) {
    if (project.id === projectId) {
      return project;
    }
  }
  return null;
}

function findFamilySplitIndex(name: string): number {
  const separators = [' – ', ' — ', ' - '];
  let best = -1;
  for (const separator of separators) {
    const index = name.indexOf(separator);
    if (index <= 0) {
      continue;
    }
    if (best < 0 || index < best) {
      best = index;
    }
  }
  return best;
}

function firstNonEmpty(values: string[] | null | undefined): string {
  if (!values || values.length === 0) {
    return '';
  }
  return values[0]?.trim() || '';
}
