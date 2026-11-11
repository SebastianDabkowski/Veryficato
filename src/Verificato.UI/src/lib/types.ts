export type Severity = 'Info' | 'Warning' | 'Error' | 'Critical';
export type RunState = 'Starting' | 'Running' | 'Stopping' | 'Completed' | 'Failed' | 'Interrupted';
export interface Project { id: string; name: string; sourcePath: string; applicationUrl: string; allowedOrigins: string[]; createdAt: string; updatedAt: string }
export interface ProjectInput { name: string; sourcePath: string; applicationUrl: string; allowedOrigins: string[] }
export interface Run { id: string; projectId: string; kind: 'Interactive' | 'CodeAnalysis'; state: RunState; outcome: 'NoIssuesDetected' | 'IssuesDetected' | 'Inconclusive'; completeness: 'CompleteWithinScope' | 'Partial' | 'Unavailable'; startedAt: string; endedAt?: string; failure?: string; limitations?: string; stopReason?: string; engineVersion: string; configuration: Project; isActive: boolean }
export interface Observation { id: string; runId: string; sequence: number; occurredAt: string; ruleId: string; category: string; severity: Severity; title: string; message: string; url?: string; filePath?: string; line?: number; column?: number; stack?: string; value?: number; unit?: string; isIssue: boolean }
export interface Issue { id: string; runId: string; ruleId: string; category: string; severity: Severity; title: string; message: string; url?: string; filePath?: string; line?: number; column?: number; stack?: string; firstSeenAt: string; lastSeenAt: string; occurrenceCount: number }
export interface RunSummary { eventCount: number; issueCount: number; errorCount: number; warningCount: number; requestCount: number; measuredRequestCount: number; medianResponseMs?: number; p95ResponseMs?: number; droppedObservations: number }
export interface RunDetails { run: Run; summary: RunSummary; issues: Issue[]; metrics: Observation[] }
export interface EventPage { items: Observation[]; nextSequence: number; hasMore: boolean }
export interface SystemStatus { version: string; browserInstalled: boolean; dataDirectory: string; analysisMode: string; maxSessionMinutes: number; maxObservations: number }
