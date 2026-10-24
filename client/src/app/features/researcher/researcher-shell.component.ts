import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { ResearcherNavItem } from '../../core/models/researcher.model';

@Component({
  selector: 'app-researcher-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TranslatePipe],
  templateUrl: './researcher-shell.component.html',
  styleUrl: './researcher-shell.component.scss'
})
export class ResearcherShellComponent {
  readonly navItems: ResearcherNavItem[] = [
    { path: '/researcher', labelKey: 'researcher.nav.overview', exact: true },
    { path: '/researcher/runs', labelKey: 'researcher.nav.runs', exact: false },
    { path: '/researcher/live', labelKey: 'researcher.nav.live', exact: false },
    { path: '/researcher/companies', labelKey: 'researcher.nav.companies', exact: false },
    { path: '/researcher/sources', labelKey: 'researcher.nav.sources', exact: false },
    { path: '/researcher/fetch', labelKey: 'researcher.nav.fetch', exact: false },
    { path: '/researcher/errors', labelKey: 'researcher.nav.errors', exact: false },
    { path: '/researcher/coverage', labelKey: 'researcher.nav.coverage', exact: false },
    { path: '/researcher/map', labelKey: 'researcher.nav.map', exact: false },
    {
      path: '/settings',
      labelKey: 'researcher.nav.settings',
      exact: false,
      queryParams: { tab: 'research' }
    }
  ];
}
