import { describe, expect, it } from 'vitest';
import { layoutGraph, structurizrDsl, visibleComponents } from './graph';
import type { ArchitectureComponent, ArchitectureDocument, ArchitectureRelationship } from './types';
const component = (id: string, isTest = false): ArchitectureComponent => ({ id, name: id, projectFile: `${id}.csproj`, projectFileId: `${id}-file`, projectFileHash: 'hash', technology: 'C# · net10.0', kind: 'Biblioteka', isTest, description: 'Inventory', descriptionBasis: 'inventory', namespaces: [], files: [], packages: [] });
const reference = (sourceId: string, targetId: string): ArchitectureRelationship => ({ id: sourceId + targetId, sourceId, targetId, description: 'Odwołuje się do', technology: 'C# / ProjectReference', evidenceFile: sourceId + '.csproj', line: 1 });
const document: ArchitectureDocument = {
  id: 'snapshot', projectId: 'project', runId: 'run', name: 'Example', sourceRoot: '/local', generatedAt: '2026-01-01T00:00:00Z', schemaVersion: '1', generator: 'test', sourceFingerprint: 'sha256',
  components: [component('app'), component('core'), component('worker'), component('tests', true)],
  containers: [{ id: 'app', name: 'Application', technology: '.NET', description: 'Inferred', isInferred: true, componentIds: ['app', 'core'] }],
  relationships: [reference('app', 'core'), reference('tests', 'app'), reference('worker', 'core')], unresolvedReferences: [], warnings: [],
};
describe('C4 component projection', () => {
  it('limits a C3 diagram to one container, excluding test components by default', () => {
    expect(visibleComponents(document, 'app', false).map(c => c.id)).toEqual(['app', 'core']);
    expect(visibleComponents(document, 'all', false)).toHaveLength(3);
    expect(visibleComponents(document, 'all', true)).toHaveLength(4);
  });
  it('lays out cycles and disconnected nodes with finite, distinct positions', () => {
    const components = document.components;
    const graph = layoutGraph(components, [reference('app', 'core'), reference('core', 'app'), reference('outside', 'core')]);
    expect(graph.width).toBeGreaterThan(250);
    expect(graph.height).toBeGreaterThan(174);
    expect(graph.positions.size).toBe(4);
    expect(graph.routes.size).toBe(2);
    for (const route of graph.routes.values()) {
      expect(route.points.length).toBeGreaterThan(1);
      for (const point of [...route.points, route.label]) { expect(Number.isFinite(point.x)).toBe(true); expect(Number.isFinite(point.y)).toBe(true); }
    }
    for (const point of graph.positions.values()) { expect(Number.isFinite(point.x)).toBe(true); expect(Number.isFinite(point.y)).toBe(true); }
    expect(new Set([...graph.positions.values()].map(p => `${p.x}/${p.y}`)).size).toBe(4);
  });
  it('exports only scoped components and directed declared references in Structurizr DSL', () => {
    const dsl = structurizrDsl(document, 'app');
    expect(dsl).toContain('component application "Components"');
    expect(dsl).toContain('c_app -> c_core "Odwołuje się do" "C# / ProjectReference"');
    expect(dsl).not.toContain('c_worker'); expect(dsl).not.toContain('c_tests');
    expect(() => structurizrDsl(document, 'all')).toThrow('pojedynczego kontenera');
  });
  it('escapes quotes, backslashes and newlines instead of treating source metadata as DSL', () => {
    const dsl = structurizrDsl({ ...document, name: 'Name"\\\nmodel { malicious }' }, 'app');
    expect(dsl.split('\n')[0]).toContain('Name\\"\\\\ model { malicious }');
    expect(dsl).toContain('Inferred boundaries: confirm deployment.');
  });
});
