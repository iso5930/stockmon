from pathlib import Path
import re
import json
import csv

root = Path(__file__).resolve().parent
doc = (root / 'SRS_기술설계서.md').read_text(encoding='utf-8')
chapters = re.findall(r'^## (\d+)\.', doc, re.M)
assert chapters == [str(i) for i in range(1, 21)], chapters
assert len(re.findall(r'^```', doc, re.M)) % 2 == 0
rows = []
for line in doc.splitlines():
    if re.match(r'^\| (FR|NFR)-\d{3} \|', line):
        cols = [v.strip() for v in line.strip('|').split('|')]
        assert len(cols) == 6, cols
        rows.append(cols + ['planned', '미실행'])
ids = [r[0] for r in rows]
assert len(ids) == len(set(ids)) == 58
for prefix, count in [('FR', 41), ('NFR', 17)]:
    assert {i for i in ids if i.startswith(prefix + '-')} == {f'{prefix}-{n:03}' for n in range(1, count+1)}
for match in re.finditer(r'\b(?:FR|NFR)-\d{3}\b', doc):
    assert match.group() in ids, match.group()
for row in rows:
    assert re.search(r'T\d{2}', row[5]), row
    assert row[2] in ('P0','P1','P2')
with (root / 'requirements.tsv').open('w', encoding='utf-8-sig', newline='') as f:
    w = csv.writer(f, delimiter='\t', lineterminator='\n')
    w.writerow(['ID','요구사항','우선순위','단계','의존성','검증방법','구현상태','증거'])
    w.writerows(rows)
with (root / 'requirements.tsv').open(encoding='utf-8-sig', newline='') as f:
    data = list(csv.reader(f, delimiter='\t'))
assert len(data) == 59 and all(len(r) == 8 for r in data)
json_blocks = re.findall(r'```json\n(.*?)\n```', doc, re.S)
parsed = [json.loads(b) for b in json_blocks]
assert len(parsed) == 2
schema = parsed[1]
assert set(schema['required']) == set(schema['properties'])
assert schema['additionalProperties'] is False
try:
    import jsonschema
    jsonschema.Draft202012Validator.check_schema(schema)
    schema_check = 'JSON Schema 문법 검증 통과'
except ImportError:
    schema_check = 'JSON 파싱/필수키 검증 통과; JSON Schema validator 미설치'
compose = re.search(r'```yaml\n(.*?)\n```', doc, re.S).group(1)
try:
    import yaml
    obj = yaml.safe_load(compose)
    assert set(obj['services']) == {'db','monitor','analysis_notify'}
    for service in obj['services'].values():
        assert set(service.get('secrets', [])) <= set(obj['secrets'])
        assert set(service.get('networks', [])) <= set(obj['networks'])
    yaml_check = 'YAML 파싱/참조 검증 통과'
except ImportError:
    yaml_check = 'YAML parser 미설치; 실기 config 검증 필요'
(root / 'compose.example.yaml').write_text('# 설계 템플릿: 앱 구현 및 NAS 실기 검증 전 실행 불가\n' + compose + '\n', encoding='utf-8')
(root / 'analysis-output.schema.json').write_text(json.dumps(schema,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
report = f'''# 문서 정합성 점검\n\n2026-10-26 KST · SRS v1.2\n\n- 본문 1~20장 순서: 통과\n- FR 41개, NFR 17개 ID 중복·누락: 없음\n- 본문 FR/NFR 참조: 정의 확인\n- 요구사항별 우선순위·단계·의존성·검증 방법: 58행 확인\n- TSV와 본문: 58행, 8열, 값 일치\n- JSON 예제 2개: 파싱 통과\n- {schema_check}\n- {yaml_check}\n- 코드 블록 닫힘: 통과\n\n이 점검은 문서 정합성 검사이다. Compose의 실제 기동, OpenAI 지원 모델과의 스키마 호환성, API 연결, 성능, 보안 및 NAS 복구 인수시험은 수행하지 않았다.\n'''
(root / '문서_정합성_점검.md').write_text(report,encoding='utf-8')
print(json.dumps({'chapters':len(chapters),'FR':41,'NFR':17,'tsv_rows':len(data)-1,'schema':schema_check,'compose':yaml_check,'document_chars':len(doc)},ensure_ascii=False))
