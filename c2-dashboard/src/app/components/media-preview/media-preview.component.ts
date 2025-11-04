import { Component, Input, Output, EventEmitter, OnChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';

@Component({
  selector: 'app-media-preview',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './media-preview.component.html',
  styleUrls: ['./media-preview.component.css']
})
export class MediaPreviewComponent implements OnChanges {
  @Input() show: boolean = false;
  @Input() mediaUrl: string = '';
  @Input() mediaType: 'video' = 'video';
  @Input() fileName: string = '';
  @Input() fileSize: number = 0;
  @Input() duration: number | undefined;

  @Output() close = new EventEmitter<void>();

  safeUrl: SafeResourceUrl = '';
  loading: boolean = true;
  error: boolean = false;

  constructor(private sanitizer: DomSanitizer) {}

  ngOnChanges() {
    if (this.mediaUrl && this.show) {
      this.loading = true;
      this.error = false;
      this.safeUrl = this.sanitizer.bypassSecurityTrustResourceUrl(this.mediaUrl);
    }
  }

  onClose() {
    this.close.emit();
  }

  onMediaLoad() {
    this.loading = false;
    this.error = false;
  }

  onMediaError() {
    this.loading = false;
    this.error = true;
  }

  formatFileSize(bytes: number): string {
    if (bytes < 1024) return bytes + ' B';
    if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(2) + ' KB';
    return (bytes / (1024 * 1024)).toFixed(2) + ' MB';
  }

  formatDuration(minutes?: number): string {
    if (!minutes) return 'Unknown';
    if (minutes < 1) return `${Math.round(minutes * 60)}s`;
    return `${minutes.toFixed(1)}min`;
  }
}
