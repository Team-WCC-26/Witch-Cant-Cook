# Addressables 기반 레시피 UI

RecipeUICatalog/RecipeVisual SO는 사용하지 않습니다.

## 시트 로딩

실제로 사용하는 GSpreadReader의 Sheets에 Class Name `IngredientCombination`, 해당 탭의 gid를 추가하세요. 현재 저장된 DataManager/GSpreadReader 프리팹에는 이 항목이 없고 로컬 IngredientCombination.json은 빈 목록입니다. 등록 후 온라인 로딩을 성공시키고 Play를 다시 시작하세요.

3행 필드명: id, recipeID, ingID, conditionFlag, amount.
`id`는 행마다 고유하고 `recipeID`는 중복될 수 있습니다. 같은 recipeID의 모든 행을 시트 순서대로 표시합니다. 같은 재료도 조리 상태/수량이 다를 수 있으므로 행을 합치거나 제거하지 않습니다.

## 아이콘 주소

Ingredient와 Recipe의 `IconName`을 Addressables의 Sprite Address와 정확히 맞추세요. 대소문자도 동일해야 합니다. 예: Carrot_Icon, Salad_icon. Label은 필수가 아닙니다. Import Settings의 Texture Type은 Sprite (2D and UI)여야 합니다. Resources 폴더는 더 이상 사용하지 않으며 아이콘을 Addressables용 폴더로 이동할 수 있습니다.

조리 아이콘 주소: 1(Cut) → Cutted_Icon, 2(Grilled) → Pan_Icon, 4(Boiled) → Pot_Icon, 8(Roasted) → Oven_Icon. Flag가 5라면 자르기와 끓이기 두 아이콘을 표시합니다. 현재 프로젝트의 Icon_Cooking 그룹 주소를 사용합니다.

로드 중에는 이름·수량·조리 텍스트를 먼저 표시하고 완료 시 이미지를 적용합니다. 실패하면 경고와 텍스트를 유지합니다. 같은 주소의 동시 요청/완료 결과를 공유하며 카드가 다른 주문으로 바뀌면 이전 로딩 결과를 무시합니다. 매니저 비활성화 시 UI를 숨긴 뒤 이 UI가 보유한 Addressables 핸들만 해제합니다. 다른 ResourceManager 핸들은 건드리지 않습니다.

## 씬

기존 DishOrderManager → Scroll Views 연결을 유지하세요. 각 카드의 Dish Image, Ingredient Prefab, Ingredient Root가 필요합니다. 재료 템플릿의 Icon, Ingredient Name, Method Images/Labels도 연결되어야 합니다. 생성 메뉴로 만든 UI는 이미 연결되어 있습니다.

주문 3개 상단 정렬, 초과 주문 대기, 가장 오래된 동일 RecipeId의 Success/Fail 제거 흐름은 유지됩니다. 시간은 Recipe.timeLimit(초) 기준입니다. 서버는 수정하지 않습니다.
