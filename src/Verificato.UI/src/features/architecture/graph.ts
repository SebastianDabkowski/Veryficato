import dagre from '@dagrejs/dagre';
import type { ArchitectureComponent, ArchitectureDocument, ArchitectureRelationship } from './types';

export const NODE_WIDTH = 250;
export const NODE_HEIGHT = 174;
export function visibleComponents(document: ArchitectureDocument, containerId: string, includeTests: boolean): ArchitectureComponent[] {
  const container = document.containers.find(c => c.id === containerId);
  return document.components.filter(c => (includeTests || !c.isTest) && (!container || container.componentIds.includes(c.id)));
}
export function layoutGraph(components: ArchitectureComponent[], relationships: ArchitectureRelationship[]) {
  const graph = new dagre.graphlib.Graph({ multigraph: true });
  const offset = (point: { x: number; y: number }) => ({ x: point.x + 40, y: point.y + 110 });
  graph.setGraph({ rankdir: 'TB', ranksep: 100, nodesep: 40, marginx: 0, marginy: 0 });
  graph.setDefaultEdgeLabel(() => ({}));
  for (const component of components) graph.setNode(component.id, { width: NODE_WIDTH, height: NODE_HEIGHT });
  const ids = new Set(components.map(c => c.id));
  for (const relation of relationships) if (ids.has(relation.sourceId) && ids.has(relation.targetId))
    graph.setEdge(relation.sourceId, relation.targetId, { width: 195, height: 22, labelpos: 'c' }, relation.id);
  dagre.layout(graph);
  return {
    positions: new Map(components.map(component => { const node = graph.node(component.id); return [component.id, offset({ x: node.x - NODE_WIDTH / 2, y: node.y - NODE_HEIGHT / 2 })] as const; })),
    routes: new Map(relationships.filter(r => ids.has(r.sourceId) && ids.has(r.targetId)).map(relation => {
      const edge = graph.edge({ v: relation.sourceId, w: relation.targetId, name: relation.id });
      return [relation.id, { points: edge.points.map(offset), label: offset({ x: edge.x ?? 0, y: edge.y ?? 0 }) }] as const;
    })),
    width: Math.max(420, (graph.graph().width ?? NODE_WIDTH) + 80),
    height: Math.max(350, (graph.graph().height ?? NODE_HEIGHT) + 150),
  };
}
const quoted = (value: string) => `"${value.replaceAll('\\', '\\\\').replaceAll('"', '\\"').replace(/[\r\n]/g, ' ')}"`;
export function structurizrDsl(document: ArchitectureDocument, containerId: string): string {
  const container = document.containers.find(c => c.id === containerId);
  if (!container) throw new Error('Eksport C3 wymaga wybrania pojedynczego kontenera.');
  const components = visibleComponents(document, containerId, false);
  const ids = new Set(components.map(c => c.id));
  const lines = [
    `workspace ${quoted(document.name)} "C4 Component diagram. Inferred boundaries: confirm deployment." {`,
    '  model {', `    system = softwareSystem ${quoted(document.name)} {`,
    `      application = container ${quoted(container.name)} ${quoted(container.description)} ${quoted(container.technology)} {`,
    ...components.map(c => `        c_${c.id} = component ${quoted(c.name)} ${quoted(c.description)} ${quoted(c.technology)}`),
    '      }', '    }',
    ...document.relationships.filter(r => ids.has(r.sourceId) && ids.has(r.targetId)).map(r =>
      `    c_${r.sourceId} -> c_${r.targetId} ${quoted(r.description + (r.condition ? ' (conditional)' : ''))} ${quoted(r.technology)}`),
    '  }', '  views {', '    component application "Components" {', '      include *', '      autoLayout tb', '    }', '  }', '}',
  ];
  return lines.join('\n');
}
