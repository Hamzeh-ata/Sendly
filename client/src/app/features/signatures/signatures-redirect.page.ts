import { Component, inject, OnInit } from '@angular/core';
import { Router } from '@angular/router';

@Component({
  selector: 'app-signatures-redirect',
  template: ''
})
export class SignaturesRedirectPage implements OnInit {
  private readonly router = inject(Router);

  ngOnInit(): void {
    this.router.navigate(['/settings'], { queryParams: { tab: 'signatures' }, replaceUrl: true });
  }
}
