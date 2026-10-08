import { Component, inject } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { BarrierDiagram } from './barrier-diagram';

@Component({
  selector: 'app-methodology',
  imports: [BarrierDiagram],
  templateUrl: './methodology.html',
})
export class Methodology {
  constructor() {
    inject(Title).setTitle('Methodology · Jev Odds');
  }
}
