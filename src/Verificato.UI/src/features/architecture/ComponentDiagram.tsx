import { useEffect, useMemo } from 'react';
import { Background, BaseEdge, Controls, Handle, MarkerType, MiniMap, Panel, Position, ReactFlow, useNodesInitialized, useReactFlow, useStore, useNodesState, type Edge, type EdgeProps, type Node, type NodeProps } from '@xyflow/react';
import { Box, FileCode2, FlaskConical, MousePointer2 } from 'lucide-react';
import type { ArchitectureComponent, ArchitectureContainer, ArchitectureRelationship } from './types';
import { layoutGraph, NODE_HEIGHT, NODE_WIDTH } from './graph';
import type { Theme } from '../../lib/theme';
import '@xyflow/react/dist/style.css';

type ComponentData = { component: ArchitectureComponent; focused: boolean; dimmed: boolean; select: (id: string) => void };
type BoundaryData = { name: string; technology: string; projection: boolean };
function ComponentNode({ data }: NodeProps<Node<ComponentData>>) {
  const c = data.component;
  return <><Handle type="target" position={Position.Top} isConnectable={false} /><button type="button" className={`c4-component ${data.focused ? 'is-focused' : ''} ${data.dimmed ? 'is-dimmed' : ''}`} aria-label={`Komponent ${c.name}`} onClick={() => data.select(c.id)}>
    <span className="c4-node-heading"><Box size={17} /><strong>{c.name}</strong>{c.isTest && <FlaskConical size={14} />}</span>
    <span className="c4-stereotype">[Component · projekt C#]</span><span className="c4-technology">{c.technology}</span><span className="c4-description">{c.description}</span>
    <span className="c4-node-footer"><FileCode2 size={12} /> {c.files.length} plików <span>·</span> {c.files.reduce((n, f) => n + f.types.length, 0)} typów</span>
  </button><Handle type="source" position={Position.Bottom} isConnectable={false} /></>;
}
function BoundaryNode({ data }: NodeProps<Node<BoundaryData>>) {
  return <div className="c4-boundary"><div><span>{data.projection ? '[Repozytorium · projekcja logiczna]' : '[Container · granica wywnioskowana]'}</span><strong>{data.name}</strong><small>{data.technology}</small></div></div>;
}
type ReferenceData = { relation: ArchitectureRelationship; points: { x: number; y: number }[]; label: { x: number; y: number } };
function ReferenceEdge({ id, data, markerEnd, style, label, labelStyle, labelBgStyle, labelBgPadding }: EdgeProps<Edge<ReferenceData>>) {
  if (!data) return null;
  const path = data.points.map((point, i) => `${i ? 'L' : 'M'} ${point.x} ${point.y}`).join(' ');
  return <BaseEdge id={id} path={path} markerEnd={markerEnd} style={style} interactionWidth={22} label={label} labelX={data.label.x} labelY={data.label.y} labelStyle={labelStyle} labelBgStyle={labelBgStyle} labelBgPadding={labelBgPadding} />;
}
function ResponsiveFit() {
  const initialized = useNodesInitialized();
  const width = useStore(state => state.width), height = useStore(state => state.height);
  const { fitView } = useReactFlow();
  useEffect(() => { if (initialized && width && height) void fitView({ padding: .1, maxZoom: 1 }); }, [initialized, width, height, fitView]);
  return null;
}
const nodeTypes = { component: ComponentNode, boundary: BoundaryNode };
const edgeTypes = { reference: ReferenceEdge };
export function ComponentDiagram({ components, relationships, container, selectedId, query, onSelect, onRelation, theme }: {
  components: ArchitectureComponent[]; relationships: ArchitectureRelationship[]; container?: ArchitectureContainer; selectedId?: string; query: string;
  onSelect: (id: string) => void; onRelation: (relation: ArchitectureRelationship) => void; theme: Theme;
}) {
  const layout = useMemo(() => layoutGraph(components, relationships), [components, relationships]);
  const diagramNodes = useMemo<Node[]>(() => [
    { id: 'boundary', type: 'boundary', position: { x: 0, y: 0 }, data: { name: container?.name ?? 'Wszystkie projekty', technology: container?.technology ?? 'C# / .NET', projection: !container },
      width: layout.width, height: layout.height, selectable: false, focusable: false, draggable: false, zIndex: -1 },
    ...components.map(component => ({ id: component.id, type: 'component', parentId: 'boundary', position: layout.positions.get(component.id)!,
      width: NODE_WIDTH, height: NODE_HEIGHT, draggable: false, focusable: false,
      data: { component, focused: selectedId === component.id, dimmed: !!query && !`${component.name} ${component.projectFile} ${component.namespaces.join(' ')}`.toLowerCase().includes(query.toLowerCase()), select: onSelect } })),
  ], [components, container, layout, selectedId, query, onSelect]);
  const [nodes, setNodes, onNodesChange] = useNodesState(diagramNodes);
  useEffect(() => setNodes(previous => diagramNodes.map(node => {
    const old = previous.find(item => item.id === node.id);
    return { ...node, measured: old?.width === node.width && old?.height === node.height ? old?.measured : undefined };
  })), [diagramNodes, setNodes]);
  const edges = useMemo<Edge<ReferenceData>[]>(() => {
    const ids = new Set(components.map(c => c.id));
    return relationships.filter(r => ids.has(r.sourceId) && ids.has(r.targetId)).map(r => ({
      id: r.id, source: r.sourceId, target: r.targetId, type: 'reference', data: { relation: r, ...layout.routes.get(r.id)! },
      label: r.condition ? 'Odwołuje się do · C# (warunkowo)' : 'Odwołuje się do · C#',
      markerEnd: { type: MarkerType.ArrowClosed, color: r.condition ? '#d5ad66' : '#79afff' },
      style: { stroke: r.condition ? '#d5ad66' : '#79afff', strokeWidth: selectedId === r.sourceId || selectedId === r.targetId ? 2.5 : 1.5,
        strokeDasharray: r.condition ? '6 4' : undefined, opacity: selectedId && selectedId !== r.sourceId && selectedId !== r.targetId ? .3 : 1 },
      labelStyle: { fill: 'var(--muted)', fontSize: 11 }, labelBgStyle: { fill: 'var(--surface)', fillOpacity: .95 },
      labelBgPadding: [8, 5] as [number, number], interactionWidth: 22,
    }));
  }, [components, relationships, selectedId, layout]);
  return <div className="c4-diagram" aria-label="Diagram komponentów C4 C3">
    <ReactFlow key={components.map(c => c.id).join(',')} nodes={nodes} edges={edges} nodeTypes={nodeTypes} edgeTypes={edgeTypes} onNodesChange={onNodesChange}
      fitView fitViewOptions={{ padding: .1, maxZoom: 1 }} minZoom={.08} maxZoom={2}
      nodesConnectable={false} nodesDraggable={false} elementsSelectable panOnDrag colorMode={theme}
      onEdgeClick={(_, edge) => { if (edge.data?.relation) onRelation(edge.data.relation); }}
      proOptions={{ hideAttribution: true }}>
      <ResponsiveFit /><Background gap={22} size={1} color="var(--border)" /><Controls showInteractive={false} /><MiniMap pannable zoomable style={{ width: 125, height: 80 }} nodeColor={node => node.id === 'boundary' ? 'transparent' : 'var(--accent)'} nodeStrokeColor="var(--accent-text)" maskColor="transparent" />
      <Panel position="top-right"><span className="c4-canvas-badge">C4 MODEL <strong>{container ? 'C3 · COMPONENT' : 'PROJEKCJA'}</strong></span></Panel>
      <Panel position="bottom-center"><span className="c4-canvas-hint"><MousePointer2 size={12} /> Kliknij komponent lub relację · przeciągnij tło · przybliż kółkiem</span></Panel>
    </ReactFlow>
  </div>;
}
