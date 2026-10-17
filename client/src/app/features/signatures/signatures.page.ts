import { Component } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { SignaturesManagerComponent } from '../settings/signatures/signatures-manager.component';

@Component({
  selector: 'app-signatures-page',
  imports: [TranslatePipe, SignaturesManagerComponent],
  templateUrl: './signatures.page.html',
  styleUrl: './signatures.page.scss'
})
export class SignaturesPage {}
